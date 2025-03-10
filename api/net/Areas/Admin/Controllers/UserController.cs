using System.Net;
using System.Net.Mime;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using TNO.API.Areas.Admin.Models.User;
using TNO.API.CSS;
using TNO.API.Models;
using TNO.DAL.Services;
using TNO.Entities;
using TNO.Entities.Models;
using TNO.Keycloak;

namespace TNO.API.Areas.Admin.Controllers;

/// <summary>
/// UserController class, provides User endpoints for the admin api.
/// </summary>
[ClientRoleAuthorize(ClientRole.Administrator)]
[ApiController]
[Area("admin")]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[area]/users")]
[Route("api/[area]/users")]
[Route("v{version:apiVersion}/[area]/users")]
[Route("[area]/users")]
[ProducesResponseType(typeof(ErrorResponseModel), (int)HttpStatusCode.Unauthorized)]
[ProducesResponseType(typeof(ErrorResponseModel), (int)HttpStatusCode.Forbidden)]
public class UserController : ControllerBase
{
    #region Variables
    private readonly IUserService _userService;
    private readonly ICssHelper _cssHelper;
    #endregion

    #region Constructors
    /// <summary>
    /// Creates a new instance of a UserController object, initializes with specified parameters.
    /// </summary>
    /// <param name="userService"></param>
    /// <param name="cssHelper"></param>
    public UserController(IUserService userService, ICssHelper cssHelper)
    {
        _userService = userService;
        _cssHelper = cssHelper;
    }
    #endregion

    #region Endpoints
    /// <summary>
    /// Find a page of user for the specified query filter.
    /// </summary>
    /// <returns></returns>
    [HttpGet]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(IPaged<UserModel>), (int)HttpStatusCode.OK)]
    [SwaggerOperation(Tags = new[] { "User" })]
    public IActionResult Find()
    {
        var uri = new Uri(this.Request.GetDisplayUrl());
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        var result = _userService.Find(new DAL.Models.UserFilter(query));
        var page = new Paged<UserModel>(result.Items.Select(ds => new UserModel(ds)), result.Page, result.Quantity, result.Total);
        return new JsonResult(page);
    }

    /// <summary>
    /// Find user for the specified 'id'.
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    [HttpGet("{id}")]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(UserModel), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(string), (int)HttpStatusCode.NoContent)]
    [SwaggerOperation(Tags = new[] { "User" })]
    public IActionResult FindById(int id)
    {
        var result = _userService.FindById(id);

        if (result == null) return new NoContentResult();
        return new JsonResult(new UserModel(result));
    }

    /// <summary>
    /// Add user for the specified 'id'.
    /// </summary>
    /// <param name="model"></param>
    /// <returns></returns>
    [HttpPost]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(UserModel), (int)HttpStatusCode.Created)]
    [ProducesResponseType(typeof(ErrorResponseModel), (int)HttpStatusCode.BadRequest)]
    [SwaggerOperation(Tags = new[] { "User" })]
    public IActionResult Add(UserModel model)
    {
        var user = (User)model;
        if (String.IsNullOrWhiteSpace(user.Key) || user.Key == Guid.Empty.ToString()) user.Key = Guid.NewGuid().ToString();
        var result = _userService.AddAndSave(user);
        return CreatedAtAction(nameof(FindById), new { id = result.Id }, new UserModel(result));
    }

    /// <summary>
    /// Update user for the specified 'id'.
    /// Update the user in Keycloak if the 'Key' is linked.
    /// </summary>
    /// <param name="model"></param>
    /// <returns></returns>
    [HttpPut("{id}")]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(UserModel), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(ErrorResponseModel), (int)HttpStatusCode.BadRequest)]
    [SwaggerOperation(Tags = new[] { "User" })]
    public async Task<IActionResult> UpdateAsync(UserModel model)
    {
        await _cssHelper.UpdateUserRolesAsync(model.Key, model.Roles.ToArray());
        var user = _userService.UpdateAndSave((Entities.User)model);
        return new JsonResult(new UserModel(user));
    }

    /// <summary>
    /// Delete user for the specified 'id'.
    /// Delete the user from Keycloak if the 'Key' is linked.
    /// </summary>
    /// <param name="model"></param>
    /// <returns></returns>
    [HttpDelete("{id}")]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(UserModel), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(ErrorResponseModel), (int)HttpStatusCode.BadRequest)]
    [SwaggerOperation(Tags = new[] { "User" })]
    public async Task<IActionResult> DeleteAsync(UserModel model)
    {
        await _cssHelper.DeleteUserAsync((User)model);
        return new JsonResult(model);
    }
    #endregion
}
