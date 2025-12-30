using Auth.Models.Data;
using Auth.Models.Entities;
using Auth.Services.Services.Seed;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;

namespace Auth.API.Seed
{
    public class SeedData
    {
        private const string DropboxCsvUrl =
             "https://www.dropbox.com/scl/fi/5iiszsu9hn3inirp5tqhc/users.csv?rlkey=lzegatdz0wlqa5gbodffmmuhi&st=id3qung4&dl=1";
        private const string DropboxMentorsCsvUrl =
             "https://www.dropbox.com/scl/fi/ixzp98v8nn0siqf3uivpq/Mentors_for_IPJ.csv?rlkey=sht5r8416h0j6mgkdzinep8hz&st=90yshjpo&dl=1";

        public static async Task SeedUsersAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<SeedData>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            logger.LogInformation("Starting user seeding...");

            var questions = await context.Questions
                .Where(q => q.IsSkill && q.Order >= 1 && q.Order <= 4)
                .ToDictionaryAsync(q => q.Order, q => q.QuestionId);

            logger.LogInformation("Downloading users CSV from Dropbox: {Url}", DropboxCsvUrl);

            using var http = new HttpClient();
            await using var csvStream = await http.GetStreamAsync(DropboxCsvUrl);
            using var reader = new StreamReader(csvStream);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HeaderValidated = null,
                MissingFieldFound = null
            });

            var records = csv.GetRecords<UserCsvRecord>().ToList();

            int createdCount = 0;
            int skippedCount = 0;

            // ✅ keep passwords in memory only
            var sb = new StringBuilder();
            using var stringWriter = new StringWriter(sb, CultureInfo.InvariantCulture);
            using var pwCsv = new CsvWriter(stringWriter, CultureInfo.InvariantCulture);

            pwCsv.WriteField("Email");
            pwCsv.WriteField("Password");
            pwCsv.NextRecord();

            var random = new Random();
            string GeneratePassword(string firstName, string lastName)
                => $"{firstName[0]}{lastName}{random.Next(10, 99)}!{(char)('A' + random.Next(0, 26))}";

            foreach (var r in records)
            {
                if (string.IsNullOrWhiteSpace(r.Email))
                {
                    logger.LogWarning("Skipped user with missing email: {First} {Last}", r.FirstName, r.LastName);
                    skippedCount++;
                    continue;
                }

                if (await userManager.FindByEmailAsync(r.Email) != null)
                {
                    logger.LogInformation("Skipped existing user: {Email}", r.Email);
                    skippedCount++;
                    continue;
                }

                var pwd = GeneratePassword(r.FirstName, r.LastName);
                var user = new User
                {
                    UserName = r.Email,
                    Email = r.Email,
                    FirstName = r.FirstName,
                    LastName = r.LastName,
                    Title = r.ScholarStatus,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    IsActive = true,
                    MustChangePassword = true
                };

                var result = await userManager.CreateAsync(user, pwd);
                if (!result.Succeeded)
                {
                    logger.LogError("Failed to create user {Email}: {Errors}", r.Email,
                        string.Join(", ", result.Errors.Select(e => e.Description)));
                    skippedCount++;
                    continue;
                }

                // add to in-memory CSV
                pwCsv.WriteField(r.Email);
                pwCsv.WriteField(pwd);
                pwCsv.NextRecord();

                var roleName = r.FirstName.Equals("Admin", StringComparison.OrdinalIgnoreCase) ? "Admin" : "User";
                if (!await userManager.IsInRoleAsync(user, roleName))
                {
                    var roleResult = await userManager.AddToRoleAsync(user, roleName);
                    if (!roleResult.Succeeded)
                        logger.LogWarning("Failed to assign role '{Role}' to {Email}: {Errors}",
                            roleName, r.Email, string.Join(", ", roleResult.Errors.Select(e => e.Description)));
                }

                var skillAnswers = new[] { r.SoftSkill, r.HardSkill, r.InterpersonalSkill, r.KnowledgeSkill };
                for (int i = 0; i < skillAnswers.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(skillAnswers[i])) continue;
                    context.Skills.Add(new Skill
                    {
                        ScholarId = user.Id,
                        QuestionId = questions[i + 1],
                        SkillAnswer = skillAnswers[i],
                        Slot = i + 1,
                        Active = true
                    });
                }

                createdCount++;
            }

            await context.SaveChangesAsync();

            // ✅ Upload final CSV to Dropbox directly
            var fileName = $"/generated-passwords-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
            await DropboxUploader.UploadTextAsync(fileName, sb.ToString());

            logger.LogInformation("User seeding finished. Created: {Created}, Skipped: {Skipped}. Passwords uploaded to Dropbox as {File}",
                createdCount, skippedCount, fileName);
        }

        public static async Task SeedMentorsAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<SeedData>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            logger.LogInformation("Starting mentor seeding...");

            logger.LogInformation("Downloading mentors CSV from Dropbox: {Url}", DropboxMentorsCsvUrl);

            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromMinutes(5);

            var csvBytes = await http.GetByteArrayAsync(DropboxMentorsCsvUrl);
            logger.LogInformation("Downloaded {Size} bytes from Dropbox", csvBytes.Length);

            using var memoryStream = new MemoryStream(csvBytes);
            using var reader = new StreamReader(memoryStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            // Log the first few lines for debugging
            var firstLine = await reader.ReadLineAsync();
            logger.LogInformation("CSV Header: {Header}", firstLine);
            memoryStream.Position = 0; // Reset stream

            using var reader2 = new StreamReader(memoryStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            using var csv = new CsvReader(reader2, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HeaderValidated = null,
                MissingFieldFound = null,
                BadDataFound = null,
                TrimOptions = CsvHelper.Configuration.TrimOptions.Trim,
                Encoding = Encoding.UTF8,
                DetectDelimiter = true,
                PrepareHeaderForMatch = args => args.Header.Trim()
            });

            var records = csv.GetRecords<MentorCsvRecord>().ToList();

            logger.LogInformation("Successfully parsed {Count} records from CSV", records.Count);

            // Log first record for debugging
            if (records.Any())
            {
                var first = records.First();
                logger.LogInformation("Sample record - Mentor: '{MentorName}' ({MentorEmail}), Scholar: '{ScholarName}' ({ScholarEmail})",
                    first.MentorName ?? "NULL",
                    first.MentorEmail ?? "NULL",
                    first.Scholar ?? "NULL",
                    first.ScholarEmail ?? "NULL");
            }

            int createdMentorsCount = 0;
            int skippedMentorsCount = 0;
            int assignedMenteesCount = 0;
            int failedAssignmentsCount = 0;

            // Keep passwords in memory only
            var sb = new StringBuilder();
            using var stringWriter = new StringWriter(sb, CultureInfo.InvariantCulture);
            using var pwCsv = new CsvWriter(stringWriter, CultureInfo.InvariantCulture);

            pwCsv.WriteField("Mentor Email");
            pwCsv.WriteField("Mentor Password");
            pwCsv.NextRecord();

            var random = new Random();
            string GeneratePassword(string firstName, string lastName)
                => $"{firstName[0]}{lastName}{random.Next(10, 99)}!{(char)('A' + random.Next(0, 26))}";

            // Dictionary to track created mentors to avoid duplicates
            var processedMentors = new Dictionary<string, User>(StringComparer.OrdinalIgnoreCase);

            foreach (var r in records)
            {
                if (string.IsNullOrWhiteSpace(r.MentorEmail))
                {
                    logger.LogWarning("Skipped mentor with missing email: {Name}", r.MentorName ?? "(null)");
                    skippedMentorsCount++;
                    continue;
                }

                User mentor;

                // Check if we already processed this mentor in this run
                if (processedMentors.ContainsKey(r.MentorEmail))
                {
                    mentor = processedMentors[r.MentorEmail];
                    logger.LogInformation("Mentor {Email} already processed in this run", r.MentorEmail);
                }
                else
                {
                    // Check if mentor already exists in database
                    mentor = await userManager.FindByEmailAsync(r.MentorEmail);

                    if (mentor == null)
                    {
                        // Create new mentor
                        var pwd = GeneratePassword(r.MentorFirstName, r.MentorLastName);
                        mentor = new User
                        {
                            UserName = r.MentorEmail,
                            Email = r.MentorEmail,
                            FirstName = r.MentorFirstName,
                            LastName = r.MentorLastName,
                            Title = "Mentor",
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow,
                            IsActive = true,
                            MustChangePassword = true
                        };

                        var result = await userManager.CreateAsync(mentor, pwd);
                        if (!result.Succeeded)
                        {
                            logger.LogError("Failed to create mentor {Email}: {Errors}", r.MentorEmail,
                                string.Join(", ", result.Errors.Select(e => e.Description)));
                            skippedMentorsCount++;
                            continue;
                        }

                        // Add to in-memory CSV
                        pwCsv.WriteField(r.MentorEmail);
                        pwCsv.WriteField(pwd);
                        pwCsv.NextRecord();

                        createdMentorsCount++;
                        logger.LogInformation("Created mentor: {Email}", r.MentorEmail);
                    }
                    else
                    {
                        logger.LogInformation("Mentor already exists: {Email}", r.MentorEmail);
                        skippedMentorsCount++;
                    }

                    // Assign Mentor role if not already assigned
                    if (!await userManager.IsInRoleAsync(mentor, "Mentor"))
                    {
                        var roleResult = await userManager.AddToRoleAsync(mentor, "Mentor");
                        if (!roleResult.Succeeded)
                        {
                            logger.LogWarning("Failed to assign Mentor role to {Email}: {Errors}",
                                r.MentorEmail, string.Join(", ", roleResult.Errors.Select(e => e.Description)));
                        }
                        else
                        {
                            logger.LogInformation("Assigned Mentor role to {Email}", r.MentorEmail);
                        }
                    }

                    processedMentors[r.MentorEmail] = mentor;
                }

                // Now assign the scholar (mentee) to this mentor
                if (!string.IsNullOrWhiteSpace(r.ScholarEmail))
                {
                    var scholar = await userManager.FindByEmailAsync(r.ScholarEmail.Trim());
                    if (scholar != null)
                    {
                        if (scholar.MentorId != mentor.Id)
                        {
                            scholar.MentorId = mentor.Id;
                            scholar.UpdatedAt = DateTime.UtcNow;

                            var updateResult = await userManager.UpdateAsync(scholar);
                            if (updateResult.Succeeded)
                            {
                                assignedMenteesCount++;
                                logger.LogInformation("Assigned scholar {ScholarEmail} to mentor {MentorEmail}",
                                    r.ScholarEmail, r.MentorEmail);
                            }
                            else
                            {
                                failedAssignmentsCount++;
                                logger.LogError("Failed to assign scholar {ScholarEmail} to mentor {MentorEmail}: {Errors}",
                                    r.ScholarEmail, r.MentorEmail,
                                    string.Join(", ", updateResult.Errors.Select(e => e.Description)));
                            }
                        }
                        else
                        {
                            logger.LogInformation("Scholar {ScholarEmail} already assigned to mentor {MentorEmail}",
                                r.ScholarEmail, r.MentorEmail);
                        }
                    }
                    else
                    {
                        failedAssignmentsCount++;
                        logger.LogWarning("Scholar not found: {ScholarEmail} for mentor {MentorEmail}",
                            r.ScholarEmail, r.MentorEmail);
                    }
                }
            }

            await context.SaveChangesAsync();

            // Upload mentor passwords to Dropbox
            if (createdMentorsCount > 0)
            {
                var mentorPasswordsFileName = $"/generated-mentor-passwords-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
                await DropboxUploader.UploadTextAsync(mentorPasswordsFileName, sb.ToString());
                logger.LogInformation("Mentor passwords uploaded to Dropbox as {File}", mentorPasswordsFileName);
            }

            logger.LogInformation(
                "Mentor seeding finished. Created mentors: {CreatedMentors}, Skipped mentors: {SkippedMentors}, " +
                "Assigned mentees: {AssignedMentees}, Failed assignments: {FailedAssignments}",
                createdMentorsCount, skippedMentorsCount, assignedMenteesCount, failedAssignmentsCount);
        }

        public static async Task SeedRolesAsync(IServiceProvider serviceProvider)
        {
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();

            string[] roleNames = { "Admin", "User", "Mentor", "ProgramManager", "VolunteeringTeam" };
            foreach (var roleName in roleNames)
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                {
                    await roleManager.CreateAsync(new IdentityRole(roleName));
                }
            }
        }
        public static async Task SeedQuestionsAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var questions = new List<Question>
            {
                new() { Order = 1, Text = "Please rate your current satisfaction level with the skill/goal that you want further improve in relation to your personal development", Type = "small", IsSkill = true },
                new() { Order = 2, Text = "Please rate your current satisfaction level with the skill/goal that you want further improve in relation to your professional development", Type = "small", IsSkill = true },
                new() { Order = 3, Text = "Please rate your current satisfaction level with the skill/goal that you want further improve in relation to your teamwork/leadership development", Type = "small", IsSkill = true },
                new() { Order = 4, Text = "Please rate your current satisfaction level with the skill/goal that you want further improve in relation to your knowledge/skill base", Type = "small", IsSkill = true },
                new() { Order = 5, Text = "How are your formal studies going?", Type = "Text", IsSkill = false },
                new() { Order = 6, Text = "How did you engage with your mentor this month to help you with your goals?", Type = "Text", IsSkill = false },
                new() { Order = 7, Text = "Are you attending your scholar team meetings?", Type = "Text", IsSkill = false },
                new() { Order = 8, Text = "What do you feel was your most impactful contribution to your team this month, and why?", Type = "Text", IsSkill = false },
                new() { Order = 9, Text = "Approximately how many hours have you dedicated to volunteering within the BHFF this month?", Type = "Text", IsSkill = false },
                new() { Order = 10, Text = "What do you think you did extremely well in this period?", Type = "Text", IsSkill = false },
                new() { Order = 11, Text = "What do you think you could have done better during this period?", Type = "Text", IsSkill = false },
                new() { Order = 12, Text = "Have any BHFF activities helped you discover new interests or passions?", Type = "Text", IsSkill = false },
                new() { Order = 13, Text = "Do you have any concerns, difficulties or personal problems for which you would like to receive the support or advice from BHFF if possible? Note: you can always directly contact HP Team Lead or Scholarship Program Manager and ask for support.", Type = "Text", IsSkill = false },
                new() { Order = 14, Text = "Which part of Scholarship did you especially like in this period?", Type = "Text", IsSkill = false },
                new() { Order = 15, Text = "What do you think BHFF could improve?", Type = "Text", IsSkill = false },
                new() { Order = 16, Text = "What was your overall satisfaction with the BHFF scholarship and experience in the previous period?", Type = "large", IsSkill = false }
            };

            foreach (var q in questions)
            {
                if (!await context.Questions.AnyAsync(x => x.Order == q.Order && x.Text == q.Text))
                {
                    context.Questions.Add(q);
                }
            }

            await context.SaveChangesAsync();
        }

    }
}
