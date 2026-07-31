using Auth.Models.Data;
using Auth.Models.DTOs;
using Auth.Models.Entities;
using Auth.Models.Request;
using Auth.Models.Constants;
using Auth.Services.Interfaces;
using Auth.Services.Interfaces.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services
{
    public class JournalService : IJournalService
    {
        private readonly ApplicationDbContext _context;
        private readonly INotificationService _notifications;
        private readonly IJournalWindowService _windows;
        private readonly ILogger<JournalService> _logger;

        public JournalService(
            ApplicationDbContext context,
            INotificationService notifications,
            IJournalWindowService windows,
            ILogger<JournalService> logger)
        {
            _context = context;
            _notifications = notifications;
            _windows = windows;
            _logger = logger;
        }

        /** --- Get all answers for a scholar for a month --- */
        public async Task<IEnumerable<Answer>> GetScholarAnswersAsync(string scholarId, string monthYear)
        {
            return await _context.Answers
                .Include(a => a.Question)
                .Where(a => a.ScholarId == scholarId && a.MonthYear == monthYear)
                .OrderBy(a => a.Question.Order)
                .ToListAsync();
        }

        /** --- Submit answers and mark month as submitted --- */
        public async Task<bool> SubmitAnswersAsync(SubmitAnswersRequest request)
        {
            if (!request.Answers.Any()) return false;

            // Loaded once so every answer written below can carry the question's wording as
            // it stands right now. See Answer.QuestionTextSnapshot for why.
            var questionIds = request.Answers.Select(a => a.QuestionId).Distinct().ToList();
            var questionsById = await _context.Questions
                .Where(q => questionIds.Contains(q.QuestionId))
                .ToDictionaryAsync(q => q.QuestionId);

            foreach (var ansDto in request.Answers)
            {
                var existing = await _context.Answers
                    .FirstOrDefaultAsync(a => a.ScholarId == request.ScholarId
                                              && a.MonthYear == request.MonthYear
                                              && a.QuestionId == ansDto.QuestionId);

                questionsById.TryGetValue(ansDto.QuestionId, out var question);

                if (existing != null)
                {
                    existing.Response = ansDto.Response;

                    // Re-stamped on edit: the snapshot records the wording the scholar was
                    // answering when they wrote this text, and on an edit that is now.
                    existing.ApplyQuestionSnapshot(question);
                    _context.Answers.Update(existing);
                }
                else
                {
                    var answer = new Answer
                    {
                        ScholarId = request.ScholarId,
                        QuestionId = ansDto.QuestionId,
                        MonthYear = request.MonthYear,
                        Response = ansDto.Response
                    };

                    answer.ApplyQuestionSnapshot(question);
                    await _context.Answers.AddAsync(answer);
                }
            }

            var saved = await _context.SaveChangesAsync();

            /** --- Mark month as submitted --- */
            var submission = await _context.JournalSubmissions
                .FirstOrDefaultAsync(js => js.ScholarId == request.ScholarId && js.MonthYear == request.MonthYear);

            if (submission == null)
            {
                submission = new JournalSubmission
                {
                    ScholarId = request.ScholarId,
                    MonthYear = request.MonthYear,
                    Submitted = true,
                    SubmittedAt = DateTime.UtcNow
                };
                await _context.JournalSubmissions.AddAsync(submission);
            }
            else
            {
                submission.Submitted = true;
                submission.SubmittedAt = DateTime.UtcNow;
                _context.JournalSubmissions.Update(submission);
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("Submitted {Count} answers and marked month {Month} as submitted for scholar {Scholar}",
                request.Answers.Count, request.MonthYear, request.ScholarId);

            await NotifySubmissionAsync(request.ScholarId, request.MonthYear);

            return saved > 0;
        }


        /// <summary>
        /// Confirms receipt to the scholar and tells their mentor.
        ///
        /// Deduped per scholar per month, because a scholar who submits, edits and submits
        /// again should not generate a second confirmation — and definitely should not send
        /// their mentor a second "they submitted" email.
        ///
        /// Deliberately never throws: a notification failure must not make a successful
        /// journal submission look like it failed. The scholar's answers are already
        /// committed by the time this runs.
        /// </summary>
        private async Task NotifySubmissionAsync(string scholarId, string monthYear)
        {
            try
            {
                var monthLabel = Notifications.JournalWindowService.TryParseMonth(monthYear, out var month)
                    ? month.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture)
                    : monthYear;

                await _notifications.CreateAsync(new CreateNotificationRequest
                {
                    UserId = scholarId,
                    MessageKey = NotificationKeys.JournalReceived,
                    Params = new Dictionary<string, string> { ["monthLabel"] = monthLabel },
                    DedupeKey = $"journal-received:{monthYear}",

                    // No email. The scholar just pressed submit and saw a success screen —
                    // an email saying the thing they watched happen has happened is exactly
                    // the sort of message that trains people to ignore the rest.
                    WantsEmail = false,
                    WantsPush = false
                });

                var scholar = await _context.Users
                    .AsNoTracking()
                    .Where(u => u.Id == scholarId)
                    .Select(u => new { u.MentorId, u.FirstName, u.LastName })
                    .FirstOrDefaultAsync();

                if (scholar?.MentorId is not { Length: > 0 } mentorId) return;

                await _notifications.CreateAsync(new CreateNotificationRequest
                {
                    UserId = mentorId,
                    MessageKey = NotificationKeys.MenteeSubmitted,
                    Params = new Dictionary<string, string>
                    {
                        ["menteeName"] = $"{scholar.FirstName} {scholar.LastName}".Trim(),
                        ["monthLabel"] = monthLabel
                    },
                    DedupeKey = $"mentee-submitted:{scholarId}:{monthYear}",
                    WantsEmail = true,
                    WantsPush = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Could not create submission notifications for {Scholar} / {Month}. " +
                    "The journal itself was saved.", scholarId, monthYear);
            }
        }

        /** --- Get all questions for a month + current answers + submitted state --- */
        public async Task<JournalMonthDto> GetQuestionsForMonthAsync(string scholarId, string monthYear)
        {
            var skills = await _context.Skills
            .Where(s => s.ScholarId == scholarId && s.Active)
            .ToListAsync();


            var questions = await _context.Questions
                .Where(q => q.Active)
                .OrderBy(q => q.Order)
                .ToListAsync();

            var answers = await _context.Answers
                .Where(a => a.ScholarId == scholarId && a.MonthYear == monthYear)
                .ToListAsync();

            // Question-first is right here — this is the form the scholar fills in, so it has
            // to offer every active question whether or not it has been answered.
            //
            // Two corrections though: where an answer exists, its snapshotted wording wins,
            // so re-opening a submitted month shows the question as it was asked rather than
            // as it reads today. And answers to questions that have since been deactivated
            // are appended, because otherwise the scholar's own writing disappears from their
            // journal the moment an admin retires a question.
            var result = questions.Select(q =>
            {
                var answer = answers.FirstOrDefault(a => a.QuestionId == q.QuestionId);
                var skill = q.IsSkill ? skills.FirstOrDefault(s => s.QuestionId == q.QuestionId)?.SkillAnswer : null;

                return new JournalQuestionDto
                {
                    QuestionId = q.QuestionId,
                    Text = answer?.ResolveQuestionText(q) ?? q.Text,
                    Type = answer?.ResolveQuestionType(q) ?? q.Type,
                    IsSkill = q.IsSkill,
                    Order = q.Order,
                    Response = answer?.Response,
                    SkillAnswer = skill
                };
            }).ToList();

            var retiredAnswers = answers
                .Where(a => questions.All(q => q.QuestionId != a.QuestionId))
                .Select(a => new JournalQuestionDto
                {
                    QuestionId = a.QuestionId,
                    Text = a.ResolveQuestionText(null),
                    Type = a.ResolveQuestionType(null),
                    IsSkill = false,
                    // Sorted last: these are historical and no longer part of the live form.
                    Order = int.MaxValue,
                    Response = a.Response,
                    SkillAnswer = null
                });

            result.AddRange(retiredAnswers);

            var submission = await _context.JournalSubmissions
                .FirstOrDefaultAsync(js => js.ScholarId == scholarId && js.MonthYear == monthYear);

            var submitted = submission?.Submitted ?? false;

            _logger.LogInformation("Fetched {Count} questions for {Scholar} in {Month}, submitted: {Submitted}",
                result.Count, scholarId, monthYear, submitted);

            return new JournalMonthDto
            {
                Questions = result,
                Submitted = submitted
            };
        }
    }
}
