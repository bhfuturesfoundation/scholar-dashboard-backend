using Auth.API.Controllers;
using Auth.API.Controllers.FLS;
using Auth.Models.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace Auth.Tests;

/// <summary>
/// Guards the access model.
///
/// The bug these exist for: ManagerController was annotated with a bare [Authorize], so
/// any authenticated account — an ordinary scholar, a mentor, an FLS speaker — could read
/// every scholar's journal by calling the endpoints directly. Nothing in the UI exposed
/// it, so nothing caught it. These tests assert the intended matrix rather than trusting
/// a reviewer to notice a missing Roles argument.
/// </summary>
public class AuthorizationTests
{
    private static AuthorizeAttribute? ControllerAuthorize<T>() =>
        typeof(T).GetCustomAttribute<AuthorizeAttribute>();

    private static AuthorizeAttribute? ActionAuthorize<T>(string methodName) =>
        typeof(T).GetMethod(methodName)?.GetCustomAttribute<AuthorizeAttribute>();

    private static IEnumerable<MethodInfo> ActionsOf<T>() =>
        typeof(T).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                 .Where(m => !m.IsSpecialName);

    // ── Journal oversight ────────────────────────────────────────────────────

    [Fact]
    public void ManagerController_IsRestrictedToJournalOversightRoles()
    {
        var attribute = ControllerAuthorize<ManagerController>();

        Assert.NotNull(attribute);
        Assert.Equal(AppRoles.JournalOversight, attribute!.Roles);
    }

    [Fact]
    public void ManagerController_DoesNotAllowScholarsOrSpeakers()
    {
        var roles = ControllerAuthorize<ManagerController>()!.Roles!.Split(',');

        Assert.DoesNotContain(AppRoles.User, roles);
        Assert.DoesNotContain(AppRoles.Mentor, roles);
        Assert.DoesNotContain(AppRoles.FLSSpeaker, roles);
        Assert.DoesNotContain(AppRoles.PartnerMember, roles);
    }

    [Fact]
    public void JournalOversight_IsExactlyAdminAndProgramManager()
    {
        Assert.Equal(new[] { AppRoles.Admin, AppRoles.ProgramManager }, AppRoles.JournalOversight.Split(','));
    }

    // ── FLS communications ───────────────────────────────────────────────────

    [Fact]
    public void CampaignController_AllowsTheCommunicationsGroup()
    {
        var attribute = ControllerAuthorize<FLSCampaignController>();

        Assert.NotNull(attribute);
        Assert.Equal(AppRoles.FlsCommunications, attribute!.Roles);
    }

    [Fact]
    public void FlsCommunications_IncludesPartnerMembers()
    {
        var roles = AppRoles.FlsCommunications.Split(',');

        Assert.Contains(AppRoles.PartnerMember, roles);
        Assert.Contains(AppRoles.FLSAdmin, roles);
        Assert.Contains(AppRoles.Admin, roles);
    }

    [Fact]
    public void FlsCommunications_ExcludesSpeakers()
    {
        // Speakers must never be able to mail the whole speaker list.
        Assert.DoesNotContain(AppRoles.FLSSpeaker, AppRoles.FlsCommunications.Split(','));
    }

    [Fact]
    public void FlsManagement_ExcludesPartnerMembers()
    {
        // Partner members send email. They do not verify uploads or edit speaker records.
        Assert.DoesNotContain(AppRoles.PartnerMember, AppRoles.FlsManagement.Split(','));
    }

    // ── Partner-member scope ─────────────────────────────────────────────────

    [Theory]
    [InlineData(nameof(FLSUploadController))]
    [InlineData(nameof(FLSMeetingController))]
    [InlineData(nameof(FLSTaskController))]
    [InlineData(nameof(FLSDocumentController))]
    public void PartnerMembers_CannotReachManagementControllers(string controllerName)
    {
        var controller = typeof(FLSCampaignController).Assembly
            .GetTypes()
            .Single(t => t.Name == controllerName);

        var roleStrings = controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.GetCustomAttribute<AuthorizeAttribute>()?.Roles)
            .Append(controller.GetCustomAttribute<AuthorizeAttribute>()?.Roles)
            .Where(r => !string.IsNullOrEmpty(r));

        foreach (var roles in roleStrings)
        {
            Assert.DoesNotContain(AppRoles.PartnerMember, roles!.Split(','));
        }
    }

    [Fact]
    public void PartnerMembers_CanReadTheSpeakerDirectory()
    {
        // Needed to choose campaign recipients.
        var attribute = ActionAuthorize<FLSAdminController>(nameof(FLSAdminController.GetAllSpeakers));

        Assert.NotNull(attribute);
        Assert.Contains(AppRoles.PartnerMember, attribute!.Roles!.Split(','));
    }

    [Fact]
    public void PartnerMembers_CannotVerifyUploads()
    {
        var attribute = ActionAuthorize<FLSAdminController>(nameof(FLSAdminController.GetPendingUploads))
                        ?? ControllerAuthorize<FLSAdminController>();

        Assert.NotNull(attribute);
        Assert.DoesNotContain(AppRoles.PartnerMember, attribute!.Roles!.Split(','));
    }

    // ── Blanket checks ───────────────────────────────────────────────────────

    [Fact]
    public void EveryFlsControllerRequiresAuthentication()
    {
        var flsControllers = typeof(FLSCampaignController).Assembly
            .GetTypes()
            .Where(t => t.Namespace == typeof(FLSCampaignController).Namespace
                        && typeof(ControllerBase).IsAssignableFrom(t));

        foreach (var controller in flsControllers)
        {
            Assert.True(
                controller.GetCustomAttribute<AuthorizeAttribute>() is not null,
                $"{controller.Name} has no [Authorize] attribute.");
        }
    }

    /// <summary>
    /// The only FLS endpoint that may be reached without a token. Speaker self-registration
    /// has to be public; anything else appearing here is a mistake, so the allow-list is
    /// asserted exactly rather than as a minimum.
    /// </summary>
    private static readonly string[] ExpectedAnonymousEndpoints =
    {
        "FLSSpeakerController.Register"
    };

    [Fact]
    public void OnlyTheDocumentedFlsEndpointsAreAnonymous()
    {
        var anonymous = typeof(FLSCampaignController).Assembly
            .GetTypes()
            .Where(t => t.Namespace == typeof(FLSCampaignController).Namespace
                        && typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(controller => controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
                .Select(m => $"{controller.Name}.{m.Name}"))
            .OrderBy(n => n)
            .ToArray();

        Assert.Equal(ExpectedAnonymousEndpoints.OrderBy(n => n).ToArray(), anonymous);
    }

    [Fact]
    public void AppRoles_ContainsEveryRoleNameUsedInAuthorizeAttributes()
    {
        // Catches a typo'd role string, which silently locks everyone out of an endpoint
        // rather than failing loudly.
        var usedRoles = typeof(FLSCampaignController).Assembly
            .GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                              .Select(m => m.GetCustomAttribute<AuthorizeAttribute>()?.Roles)
                              .Append(t.GetCustomAttribute<AuthorizeAttribute>()?.Roles))
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .SelectMany(r => r!.Split(','))
            .Select(r => r.Trim())
            .Distinct();

        foreach (var role in usedRoles)
        {
            Assert.True(AppRoles.All.Contains(role), $"Unknown role '{role}' used in an [Authorize] attribute.");
        }
    }

    [Fact]
    public void PartnerMemberRole_IsSeeded()
    {
        Assert.Contains(AppRoles.PartnerMember, AppRoles.All);
    }
}
