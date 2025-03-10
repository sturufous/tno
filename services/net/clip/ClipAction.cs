using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TNO.API.Areas.Services.Models.ContentReference;
using TNO.API.Areas.Services.Models.Ingest;
using TNO.Core.Exceptions;
using TNO.Core.Extensions;
using TNO.Entities;
using TNO.Models.Extensions;
using TNO.Kafka.Models;
using TNO.Services.Clip.Config;
using TNO.Services.Command;
using System.Diagnostics;

namespace TNO.Services.Clip;

/// <summary>
/// ClipAction class, performs the clip ingestion action.
/// Fetch clip feed.
/// Send message to Kafka.
/// Inform api of new content.
/// </summary>
/// <link url="https://shotstack.io/learn/use-ffmpeg-to-trim-video/"/>
public class ClipAction : CommandAction<ClipOptions>
{
    #region Variables
    private static readonly Regex ParseFileName = new("^(?<hours>[0-9]+)-(?<minutes>[0-9]+)-(?<seconds>[0-9]+)-.*");
    #endregion

    #region Properties
    #endregion

    #region Constructors
    /// <summary>
    /// Creates a new instance of a ClipAction, initializes with specified parameters.
    /// </summary>
    /// <param name="api"></param>
    /// <param name="options"></param>
    /// <param name="logger"></param>
    public ClipAction(IApiService api, IOptions<ClipOptions> options, ILogger<ClipAction> logger) : base(api, options, logger)
    {
    }
    #endregion

    #region Methods
    /// <summary>
    /// Perform the ingestion service action.
    /// Checks if a content reference has already been created for each clip before deciding whether to import it or not.
    /// Sends message to kafka if content has been added or updated.
    /// Informs API of content reference status.
    /// </summary>
    /// <param name="manager"></param>
    /// <param name="name"></param>
    /// <param name="data"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override async Task PerformActionAsync<T>(IIngestServiceActionManager manager, string? name = null, T? data = null, CancellationToken cancellationToken = default) where T : class
    {
        this.Logger.LogDebug("Performing ingestion service action for data source '{name}'", manager.Ingest.Name);

        // Each schedule will have its own process.
        foreach (var schedule in GetSchedules(manager.Ingest))
        {
            try
            {
                var process = await GetProcessAsync(manager, schedule);

                // Fetch content reference.
                var content = CreateContentReference(manager.Ingest, schedule);
                var reference = await this.FindContentReferenceAsync(content.Source, content.Uid);

                // Override the original action name based on the schedule.
                name = manager.VerifySchedule(schedule) ? "start" : "stop";

                // The assumption is that if a file has been created it was successfully generated.
                // TODO: Handle failures when a clip file was created but error'ed out.
                if (name == "start" && !IsRunning(process) && !FileExists(manager, schedule))
                {
                    if (reference == null)
                    {
                        reference = await this.Api.AddContentReferenceAsync(content);
                    }
                    else if (reference.Status == (int)WorkflowStatus.InProgress && reference.UpdatedOn?.AddMinutes(2) < DateTime.UtcNow)
                    {
                        // If another process has it in progress only attempt to do an import if it's
                        // more than an 2 minutes old. Assumption is that it is stuck.
                        reference = await this.UpdateContentReferenceAsync(reference, WorkflowStatus.InProgress);
                    }
                    else reference = null;

                    if (reference != null)
                    {
                        // TODO: Waiting for each clip to complete isn't ideal.  It needs to handle multiple processes.  However it is pretty quick at creating clips.
                        await RunProcessAsync(process, cancellationToken);
                        await this.ContentReceivedAsync(manager, reference, CreateSourceContent(process, manager.Ingest, schedule, reference));
                    }
                }
                else if (name == "stop")
                {
                    await StopProcessAsync(process, cancellationToken);
                    RemoveProcess(manager, schedule);
                }
            }
            catch (Exception ex)
            {
                if ((ex is MissingFileException || (ex is AggregateException && ex.InnerException is MissingFileException)) &&
                    !manager.Ingest.GetConfigurationValue<bool>("throwOnMissingFile")) continue;

                throw;
            }
        }
    }

    /// <summary>
    /// FFMPEG sends all logs to the error output.  There isn't a way to tell the difference regrettably.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="manager"></param>
    /// <param name="e"></param>
    protected override void OnErrorReceived(object? sender, IIngestServiceActionManager? manager, DataReceivedEventArgs e)
    {
        if (!String.IsNullOrWhiteSpace(e.Data))
        {
            this.Logger.LogInformation("{data}", e.Data);
        }
    }

    /// <summary>
    /// Stop the specified process.
    /// </summary>
    /// <param name="process"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    protected override async Task StopProcessAsync(ICommandProcess process, CancellationToken cancellationToken = default)
    {
        var args = process.Process.StartInfo.Arguments;
        this.Logger.LogInformation("Stopping process for command '{args}'", args);
        if (IsRunning(process) && !process.Process.HasExited)
        {
            var writer = process.Process.StandardInput;
            await writer.WriteLineAsync("q");
            await process.Process.WaitForExitAsync(cancellationToken);
        }
        process.Process.Dispose();
    }

    /// <summary>
    /// Create a content reference for this clip.
    /// </summary>
    /// <param name="ingest"></param>
    /// <param name="schedule"></param>
    /// <returns></returns>
    private ContentReferenceModel CreateContentReference(IngestModel ingest, ScheduleModel schedule)
    {
        var today = GetDateTimeForTimeZone(ingest);
        var publishedOn = new DateTime(today.Year, today.Month, today.Day, 0, 0, 0, today.Kind);
        if (schedule.StartAt.HasValue)
            publishedOn = publishedOn.Add(schedule.StartAt.Value);
        return new ContentReferenceModel()
        {
            Source = ingest.Source?.Code ?? throw new InvalidOperationException($"Ingest '{ingest.Name}' missing source code."),
            Uid = $"{schedule.Name}:{schedule.Id}-{publishedOn:yyyy-MM-dd-hh-mm-ss}",
            PublishedOn = this.ToTimeZone(publishedOn, ingest).ToUniversalTime(),
            Topic = ingest.Topic,
            Status = (int)WorkflowStatus.InProgress
        };
    }

    /// <summary>
    /// Send message to kafka with new source content.
    /// </summary>
    /// <param name="process"></param>
    /// <param name="ingest"></param>
    /// <param name="schedule"></param>
    /// <param name="reference"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    private SourceContent? CreateSourceContent(ICommandProcess process, IngestModel ingest, ScheduleModel schedule, ContentReferenceModel reference)
    {
        if (reference == null) return null;

        var publishedOn = reference.PublishedOn ?? DateTime.UtcNow;
        var file = (string)process.Data["filename"];
        var path = file.Replace(this.Options.VolumePath, "");
        var contentType = ingest.IngestType?.ContentType ?? throw new InvalidOperationException($"Ingest '{ingest.Name}' is missing ingest content type.");
        var content = new SourceContent(
            this.Options.DataLocation,
            reference.Source,
            contentType,
            ingest.ProductId,
            reference.Uid,
            $"{schedule.Name} {schedule.StartAt:c}-{schedule.StopAt:c}",
            "",
            "",
            publishedOn.ToUniversalTime())
        {
            StreamUrl = ingest.GetConfigurationValue("url") ?? "",
            FilePath = path?.MakeRelativePath() ?? "",
            Language = ingest.GetConfigurationValue("language") ?? ""
        };
        return content;
    }

    /// <summary>
    /// Check if the clip file contains a video stream.
    /// </summary>
    /// <param name="file"></param>
    /// <returns></returns>
    private async Task<bool> IsVideoAsync(string file)
    {
        var process = new System.Diagnostics.Process();
        process.StartInfo.Verb = $"Stream Type";
        process.StartInfo.FileName = "/bin/sh";
        process.StartInfo.Arguments = $"-c \"ffmpeg -i '{file}' 2>&1 | grep Video | awk '{{print $0}}' | tr -d ,\"";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.EnableRaisingEvents = true;
        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return !String.IsNullOrWhiteSpace(output);
    }

    /// <summary>
    /// Only return schedules that have passed and are within the 'ScheduleLimiter' configuration setting.
    /// </summary>
    /// <param name="ingest"></param>
    /// <returns></returns>
    protected override IEnumerable<ScheduleModel> GetSchedules(IngestModel ingest)
    {
        var keepChecking = bool.Parse(ingest.GetConfigurationValue("keepChecking"));
        var now = GetDateTimeForTimeZone(ingest).TimeOfDay;
        return ingest.IngestSchedules.Where(s =>
            s.Schedule != null &&
            s.Schedule.StopAt != null &&
            s.Schedule.StopAt.Value <= now &&
           (keepChecking || s.Schedule.StopAt.Value.Add(this.Options.ScheduleLimiter) >= now)
        ).Select(s => s.Schedule!);
    }

    /// <summary>
    /// Check if the file already exists.
    /// </summary>
    /// <param name="manager"></param>
    /// <param name="schedule"></param>
    /// <returns></returns>
    private bool FileExists(IIngestServiceActionManager manager, ScheduleModel schedule)
    {
        var output = GetOutput(manager.Ingest, schedule);
        return File.Exists(output);
    }

    /// <summary>
    /// Generate the command arguments for the service action.
    /// </summary>
    /// <param name="process"></param>
    /// <param name="manager"></param>
    /// <param name="schedule"></param>
    /// <returns></returns>
    protected override async Task<string> GenerateCommandArgumentsAsync(ICommandProcess process, IIngestServiceActionManager manager, ScheduleModel schedule)
    {
        if (schedule == null) throw new InvalidOperationException($"Ingest schedule '{manager.Ingest.Name}' is required");

        var logLevel = manager.Ingest.GetConfigurationValue<string>("logLevel", "warning");
        var input = await GetInputFileAsync(manager.Ingest, schedule);
        var start = GetStart(manager.Ingest, schedule, input);
        var duration = GetDuration(schedule);
        var format = GetFormat(manager.Ingest);
        var volume = GetVolume(manager.Ingest);
        var otherArgs = GetOtherArgs(manager.Ingest);
        var copy = GetCopy(manager.Ingest);

        var output = GetOutput(manager.Ingest, schedule);
        process.Data.Add("filename", output);

        return $"-loglevel {logLevel} -i \"{input}\"{start}{duration}{volume}{format}{otherArgs}{copy} -y \"{output}\"";
    }

    /// <summary>
    /// Capture files are stored with their 'StartAt' time in their name.
    /// Extract this value to find the right file.
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    private static TimeSpan ParseTimeFromFileName(string name)
    {
        var match = ParseFileName.Match(name);
        if (!match.Success) return new TimeSpan();

        var hours = int.Parse(match.Groups["hours"]?.Value ?? "0");
        var minutes = int.Parse(match.Groups["minutes"]?.Value ?? "0");
        var seconds = int.Parse(match.Groups["seconds"]?.Value ?? "0");

        return new TimeSpan(hours, minutes, seconds);
    }

    /// <summary>
    /// Get the path to the captured files.
    /// </summary>
    /// <param name="ingest"></param>
    /// <param name="schedule"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    private async Task<string> GetInputFileAsync(IngestModel ingest, ScheduleModel schedule)
    {
        // TODO: Handle issue where capture failed and has multiple files.
        var path = this.Options.VolumePath.CombineWith(ingest.SourceConnection?.GetConfigurationValue("path")?.MakeRelativePath() ?? "", $"{ingest.Source?.Code}/{GetDateTimeForTimeZone(ingest):yyyy-MM-dd}");
        var clipStart = schedule.StartAt;
        var filter = ingest.GetConfigurationValue("sourceFile");

        // Review each file that was captured to determine which one is valid for this clip schedule.
        foreach (var file in Directory.GetFiles(path)
            .Where(f => String.IsNullOrWhiteSpace(filter) || f.EndsWith(filter))
            .Where(f => ParseTimeFromFileName(Path.GetFileName(f)) <= clipStart))
        {
            // The offset is before the source file, so we can't use it.
            var offset = CalcStartOffset(ingest, schedule, file);
            if (offset.TotalMinutes < -1)
            {
                this.Logger.LogWarning("Ingest '{name}' schedule '{id}.{name}' capture file start is after the requested 'StartAt'.  Missing {TotalSeconds:n0} seconds.", ingest.Name, schedule.Id, schedule.Name, offset.Duration().TotalSeconds);
                continue;
            }

            // Return the first file that is long enough.
            var fileDuration = await GetDurationAsync(file);
            if (fileDuration >= schedule.CalcDuration().TotalSeconds) return file;

            this.Logger.LogWarning("Ingest '{name}' schedule '{id}:{name}' capture file duration is less than the requested duration", ingest.Name, schedule.Id, schedule.Name);
        }

        throw new MissingFileException($"Ingest '{ingest.Name}' schedule '{schedule.Id}:{schedule.Name}' ingest.Name, capture file not found or duration not long enough'");
    }

    /// <summary>
    /// Extract the file created date and time.
    /// Calculate the start offset for the schedule 'StartAt' based on the file created time.
    /// </summary>
    /// <param name="ingest"></param>
    /// <param name="schedule"></param>
    /// <param name="inputFile"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    private static TimeSpan CalcStartOffset(IngestModel ingest, ScheduleModel schedule, string inputFile)
    {
        if (schedule.StartAt == null) throw new InvalidOperationException($"Ingest schedule must have a 'StartAt' configured for {schedule.Id}:{schedule.Name}");
        if (schedule.StopAt == null) throw new InvalidOperationException($"Ingest schedule must have a 'StopAt' configured for {schedule.Id}:{schedule.Name}");

        // Linux doesn't have the file created time value, which means we need to get it from convention.
        var fileName = Path.GetFileName(inputFile);
        var createdOn = ParseTimeFromFileName(fileName);
        //var createdOn = GetLocalDateTime(ingest, File.GetCreationTimeUtc(inputFile)).TimeOfDay;
        var clipStartAt = schedule.StartAt.Value;
        return clipStartAt.Subtract(createdOn);
    }

    /// <summary>
    /// Get the start offset position within the file.
    /// A capture file may not have been created at it's start time regrettably, so clips must account for this by using the created time of the file.
    /// </summary>
    /// <param name="ingest"></param>
    /// <param name="schedule"></param>
    /// <param name="inputFile"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    private static string GetStart(IngestModel ingest, ScheduleModel schedule, string inputFile)
    {
        var start = CalcStartOffset(ingest, schedule, inputFile);
        var hours = start.Hours < 0 ? 0 : start.Hours;
        var minutes = start.Minutes < 0 ? 0 : start.Minutes;
        var seconds = start.Seconds < 0 ? 0 : start.Seconds;
        return $" -ss {hours}:{minutes}:{seconds}";
    }

    /// <summary>
    /// Get the duration of the file in seconds.
    /// Handles different types of A/V files (i.e. mkv, mp4, mp3).
    /// </summary>
    /// <param name="inputFile"></param>
    /// <returns></returns>
    private static async Task<long> GetDurationAsync(string inputFile)
    {
        var ext = Path.GetExtension(inputFile);

        switch (ext)
        {
            case ".mkv":
                var tempFile = await RepackageFileAsync(inputFile);
                var duration = await ParseDurationAsync(tempFile);
                File.Delete(tempFile);
                return duration;
            default:
                return await ParseDurationAsync(inputFile);
        }
    }

    /// <summary>
    /// Parse the duration of the A/V file from its meta-data.
    /// </summary>
    /// <param name="inputFile"></param>
    /// <returns></returns>
    private static async Task<long> ParseDurationAsync(string inputFile)
    {
        var process = new System.Diagnostics.Process();
        process.StartInfo.Verb = $"Duration";
        process.StartInfo.FileName = "/bin/sh";
        process.StartInfo.Arguments = $"-c \"ffprobe -i '{inputFile}' -show_format -v quiet | sed -n 's/duration=//p'\"";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.EnableRaisingEvents = true;
        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (String.IsNullOrWhiteSpace(output)) return 0;
        return float.TryParse(output, out float value) ? (long)Math.Floor(value) : 0;
    }

    /// <summary>
    /// Repackage the file into a different type.
    /// </summary>
    /// <param name="inputFile"></param>
    /// <param name="format"></param>
    /// <returns></returns>
    private static async Task<string> RepackageFileAsync(string inputFile, string format = "mp4")
    {
        var ext = Path.GetExtension(inputFile);
        var outputFile = inputFile.Replace(ext, $".{format}");
        var process = new System.Diagnostics.Process();
        process.StartInfo.Verb = $"Repackage";
        process.StartInfo.FileName = "/bin/sh";
        process.StartInfo.Arguments = $"-c \"ffmpeg -i '{inputFile}' -vcodec copy -acodec copy '{outputFile}'";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.EnableRaisingEvents = true;
        process.Start();

        await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return outputFile;
    }

    /// <summary>
    /// Get the duration of the clip.
    /// </summary>
    /// <param name="schedule"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    private static string GetDuration(ScheduleModel schedule)
    {
        var duration = schedule.CalcDuration();
        return $" -t {duration.Hours}:{duration.Minutes}:{duration.Seconds}";
    }

    /// <summary>
    /// Get the output path to store the file.
    /// </summary>
    /// <param name="ingest"></param>
    /// <returns></returns>
    protected string GetOutputPath(IngestModel ingest)
    {
        return this.Options.VolumePath.CombineWith(ingest.DestinationConnection?.GetConfigurationValue("path")?.MakeRelativePath() ?? "", $"{ingest.Source?.Code}/{GetDateTimeForTimeZone(ingest):yyyy-MM-dd}");
    }

    /// <summary>
    /// Get the file name from the connection settings.
    /// This will generate a unique name for each time it has to start.
    /// </summary>
    /// <param name="ingest"></param>
    /// <param name="schedule"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    private string GetOutput(IngestModel ingest, ScheduleModel schedule)
    {
        var path = GetOutputPath(ingest);
        Directory.CreateDirectory(path);

        var value = ingest.GetConfigurationValue("fileName");
        var filename = String.IsNullOrWhiteSpace(value) ? $"{schedule.Name}.mp3" : value.Replace("{schedule.Name}", schedule.Name);
        var name = Path.GetFileNameWithoutExtension(filename);
        var ext = Path.GetExtension(filename);

        return path.CombineWith($"{name}{ext}");
    }

    /// <summary>
    /// Get the format from the connection settings.
    /// </summary>
    /// <param name="ingest"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    private static string GetFormat(IngestModel ingest)
    {
        var value = ingest.GetConfigurationValue("format");
        return String.IsNullOrWhiteSpace(value) ? String.Empty : $" -f {value}";
    }

    /// <summary>
    /// Get the volume from the connection settings.
    /// </summary>
    /// <param name="ingest"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    private static string GetVolume(IngestModel ingest)
    {
        var value = ingest.GetConfigurationValue("volume");
        return String.IsNullOrWhiteSpace(value) ? String.Empty : $" -filter:a 'volume={value}'";
    }

    /// <summary>
    /// Get the other arguments from the connection settings.
    /// </summary>
    /// <param name="ingest"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    private static string GetOtherArgs(IngestModel ingest)
    {
        var value = ingest.GetConfigurationValue("otherArgs");
        return String.IsNullOrWhiteSpace(value) ? "" : $" {value}";
    }

    /// <summary>
    /// Get the copy command from the connection settings.
    /// </summary>
    /// <param name="ingest"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    private static string GetCopy(IngestModel ingest)
    {
        var value = ingest.GetConfigurationValue("copy");
        return String.IsNullOrWhiteSpace(value) ? " -c:v copy -c:a copy" : $" {value}";
    }
    #endregion
}
