namespace Auth.Models.Constants
{
    /// <summary>
    /// Every role name in the platform, plus the comma-separated groupings used in
    /// <c>[Authorize(Roles = ...)]</c>.
    ///
    /// Role strings were previously duplicated as literals across ~30 controller
    /// attributes, which is how <c>ManagerController</c> ended up with no role filter at
    /// all. Centralising them makes the access model reviewable in one place and turns a
    /// typo into a compile error instead of a silently open endpoint.
    ///
    /// The values must stay in sync with the roles created by <c>SeedData.SeedRolesAsync</c>.
    /// </summary>
    public static class AppRoles
    {
        public const string Admin = "Admin";
        public const string User = "User";
        public const string Mentor = "Mentor";
        public const string ProgramManager = "ProgramManager";
        public const string VolunteeringTeam = "VolunteeringTeam";
        public const string FLSSpeaker = "FLSSpeaker";
        public const string FLSAdmin = "FLSAdmin";

        /// <summary>
        /// Partnerships staff. Scoped to outbound communication: they compose and send
        /// email to speakers and FLS staff, and can read the recipient directory needed to
        /// do that. They deliberately cannot verify uploads, manage meetings, or edit
        /// speaker records — that stays with FLSAdmin.
        /// </summary>
        public const string PartnerMember = "PartnerMember";

        public static readonly string[] All =
        {
            Admin, User, Mentor, ProgramManager, VolunteeringTeam,
            FLSSpeaker, FLSAdmin, PartnerMember
        };

        /// <summary>Full control of the FLS portal.</summary>
        public const string FlsManagement = Admin + "," + FLSAdmin;

        /// <summary>May send FLS email and read the recipient directory.</summary>
        public const string FlsCommunications = Admin + "," + FLSAdmin + "," + PartnerMember;

        /// <summary>May read scholar journals belonging to other users.</summary>
        public const string JournalOversight = Admin + "," + ProgramManager;

        /// <summary>Any account that belongs to the FLS portal rather than the scholar app.</summary>
        public const string FlsPortal = Admin + "," + FLSAdmin + "," + PartnerMember + "," + FLSSpeaker;

        /// <summary>Speakers acting on their own records, plus the staff who oversee them.</summary>
        public const string FlsSpeakerOrManagement = FLSSpeaker + "," + Admin + "," + FLSAdmin;
    }
}
