using TNO.API.Models;

namespace TNO.API.Areas.Editor.Models.Content;

/// <summary>
/// ContentModel class, provides a model that represents an print content.
/// </summary>
public class PrintContentModel : AuditColumnsModel
{
    #region Properties
    /// <summary>
    /// get/set -
    /// </summary>
    public string Edition { get; set; } = "";

    /// <summary>
    /// get/set -
    /// </summary>
    public string Section { get; set; } = "";

    /// <summary>
    /// get/set -
    /// </summary>
    public string Byline { get; set; } = "";
    #endregion

    #region Constructors
    /// <summary>
    /// Creates a new instance of an PrintContentModel.
    /// </summary>
    public PrintContentModel() { }

    /// <summary>
    /// Creates a new instance of an PrintContentModel, initializes with specified parameter.
    /// </summary>
    /// <param name="entity"></param>
    public PrintContentModel(Entities.PrintContent entity) : base(entity)
    {
        this.Edition = entity?.Edition ?? throw new ArgumentNullException(nameof(entity));
        this.Section = entity.Section;
        this.Byline = entity.Byline;
    }

    /// <summary>
    /// Creates a new instance of an PrintContentModel, initializes with specified parameter.
    /// </summary>
    /// <param name="entity"></param>
    public PrintContentModel(Entities.Content entity) : base(entity)
    {
        if (entity?.PrintContent == null) throw new ArgumentNullException(nameof(entity));

        this.Edition = entity.PrintContent.Edition;
        this.Section = entity.PrintContent.Section;
        this.Byline = entity.PrintContent.Byline;
    }
    #endregion
}
