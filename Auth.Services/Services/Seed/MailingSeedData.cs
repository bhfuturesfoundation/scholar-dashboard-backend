using Auth.Models.Data;
using Auth.Models.Entities.Mailing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services.Seed
{
    /// <summary>
    /// Seeds a starting firm taxonomy and two example templates.
    ///
    /// The keywords matter as much as the names: they are what
    /// <c>FirmCategorizer</c> matches against, so a type seeded without them classifies
    /// nothing. Both English and Bosnian terms are included because a directory of BH firms
    /// contains both, and folded matching means diacritics don't need separate entries.
    ///
    /// Everything seeded is marked IsSystem, so it can be renamed and re-keyworded by the
    /// team but not deleted out from under an existing campaign.
    /// </summary>
    public static class MailingSeedData
    {
        private record GroupSeed(string Name, string Slug, string Color, int Order, TypeSeed[] Types);
        private record TypeSeed(string Name, string Slug, string Keywords);

        private static readonly GroupSeed[] Groups =
        {
            new("Financial", "financial", "#1d4ed8", 1, new[]
            {
                new TypeSeed("Bank", "bank",
                    "bank,banka,banke,banking,bankarstvo,raiffeisen,unicredit,sparkasse,intesa,asa banka,nlb,addiko,procredit,ziraat,bbi"),
                new TypeSeed("Insurance", "insurance",
                    "insurance,osiguranj,osiguravajuc,reosiguranj,uniqa,triglav,euroherc,grawe"),
                new TypeSeed("Microcredit & leasing", "microcredit-leasing",
                    "mikrokredit,mikrokreditna,leasing,lizing,kreditna,stedionica,savings"),
                new TypeSeed("Accounting & audit", "accounting-audit",
                    "accounting,racunovodstv,knjigovodstv,revizij,audit,revizorsk,deloitte,pwc,kpmg,ernst"),
            }),

            new("Healthcare", "healthcare", "#059669", 2, new[]
            {
                new TypeSeed("Hospital", "hospital",
                    "hospital,bolnica,klinicki centar,kcus,univerzitetski klinicki,opsta bolnica,klinika"),
                new TypeSeed("Clinic & polyclinic", "clinic",
                    "clinic,klinik,poliklinik,ordinacija,ambulanta,dom zdravlja,medical centre,medical center"),
                new TypeSeed("Pharmacy & pharma", "pharmacy",
                    "pharmacy,apoteka,ljekarna,farmaceut,pharma,bosnalijek,hemofarm,farmavita"),
                new TypeSeed("Dental", "dental",
                    "dental,stomatologija,stomatoloska,zubar,dentist,ordinacija dr"),
            }),

            new("Legal & professional", "legal-professional", "#7c3aed", 3, new[]
            {
                new TypeSeed("Law firm", "law-firm",
                    "law,legal,advokat,odvjetni,pravna,attorney,lawyer,notar,biljezni"),
                new TypeSeed("Consulting", "consulting",
                    "consulting,konsalting,konzalting,advisory,savjetovanje,consultancy,management consulting"),
                new TypeSeed("Recruitment & HR", "recruitment-hr",
                    "recruitment,zaposljavanje,hr,human resources,ljudski resursi,staffing,headhunt,agencija za zaposljavanje"),
            }),

            new("Technology", "technology", "#0891b2", 4, new[]
            {
                new TypeSeed("IT company", "it-company",
                    "it,software,softver,tech,technology,tehnologija,digital,digitalni,web,informatika,informaticke,solutions,systems,dev,studio,labs,codes,coding"),
                new TypeSeed("Telecom", "telecom",
                    "telecom,telekom,telekomunikacije,bh telecom,mtel,ht eronet,mobile,mobilna,internet provider"),
                new TypeSeed("Startup & innovation", "startup",
                    "startup,start up,inovacije,innovation,incubator,inkubator,accelerator,akcelerator,hub"),
            }),

            new("Industry & trade", "industry-trade", "#ea580c", 5, new[]
            {
                new TypeSeed("Manufacturing", "manufacturing",
                    "manufacturing,proizvodnja,fabrika,tvornica,industrija,industrijska,metal,plastika,tekstil"),
                new TypeSeed("Construction", "construction",
                    "construction,gradnja,gradjevina,gradjevinska,izgradnja,builders,inzenjering,engineering"),
                new TypeSeed("Retail & wholesale", "retail-wholesale",
                    "retail,trgovina,trgovacka,maloprodaja,veleprodaja,wholesale,market,marketi,shop,konzum,bingo"),
                new TypeSeed("Energy & utilities", "energy-utilities",
                    "energy,energija,elektroprivreda,elektro,gas,plin,vodovod,utilities,obnovljivi,renewable,solar"),
                new TypeSeed("Transport & logistics", "transport-logistics",
                    "transport,logistika,logistics,spedicija,shipping,cargo,prevoz,dostava"),
            }),

            new("Education & public", "education-public", "#be123c", 6, new[]
            {
                new TypeSeed("University & school", "education",
                    "university,univerzitet,sveuciliste,fakultet,faculty,skola,school,college,gimnazija,academy,akademija"),
                new TypeSeed("NGO & foundation", "ngo-foundation",
                    "ngo,udruzenje,udruga,fondacija,foundation,nevladina,humanitarna,association,institut"),
                new TypeSeed("Government & public body", "government",
                    "ministarstvo,ministry,opstina,opcina,municipality,kanton,vlada,government,agencija,zavod,institucija,komora,chamber"),
            }),

            new("Hospitality & media", "hospitality-media", "#0d9488", 7, new[]
            {
                new TypeSeed("Hotel & tourism", "hotel-tourism",
                    "hotel,hoteli,turizam,tourism,travel,putovanja,hostel,resort,apartmani,restoran,restaurant,catering"),
                new TypeSeed("Media & marketing", "media-marketing",
                    "media,mediji,marketing,advertising,reklamna,agencija,pr,communications,komunikacije,production,produkcija,radio,televizija"),
            }),
        };

        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var createdGroups = 0;
            var createdTypes = 0;

            foreach (var groupSeed in Groups)
            {
                var group = await context.FirmGroups.FirstOrDefaultAsync(g => g.Slug == groupSeed.Slug);

                if (group is null)
                {
                    group = new FirmGroup
                    {
                        Name = groupSeed.Name,
                        Slug = groupSeed.Slug,
                        ColorHex = groupSeed.Color,
                        SortOrder = groupSeed.Order,
                        IsSystem = true
                    };

                    context.FirmGroups.Add(group);
                    await context.SaveChangesAsync();
                    createdGroups++;
                }

                var order = 1;

                foreach (var typeSeed in groupSeed.Types)
                {
                    var type = await context.FirmTypes.FirstOrDefaultAsync(t => t.Slug == typeSeed.Slug);

                    if (type is null)
                    {
                        context.FirmTypes.Add(new FirmType
                        {
                            Name = typeSeed.Name,
                            Slug = typeSeed.Slug,
                            FirmGroupId = group.Id,
                            MatchKeywords = typeSeed.Keywords,
                            ColorHex = groupSeed.Color,
                            SortOrder = order,
                            IsSystem = true
                        });

                        createdTypes++;
                    }

                    order++;
                }
            }

            await context.SaveChangesAsync();

            await SeedTemplatesAsync(context);

            if (createdGroups > 0 || createdTypes > 0)
            {
                logger.LogInformation(
                    "Mailing taxonomy seeded: {Groups} group(s), {Types} type(s).", createdGroups, createdTypes);
            }
        }

        /// <summary>
        /// Two starter templates, each with both variants filled in so the dual-variant
        /// mechanism is visible and editable rather than something to discover from docs.
        /// </summary>
        private static async Task SeedTemplatesAsync(ApplicationDbContext context)
        {
            if (await context.MailingTemplates.AnyAsync()) return;

            var lawFirmTypeId = await context.FirmTypes
                .Where(t => t.Slug == "law-firm")
                .Select(t => (int?)t.Id)
                .FirstOrDefaultAsync();

            context.MailingTemplates.AddRange(
                new MailingTemplate
                {
                    Name = "Partnership introduction (generic)",
                    Description = "First contact with a prospective partner. Works for any firm type.",
                    IsActive = true,
                    PersonVariantEnabled = true,

                    SubjectFirmVariant = "Partnership opportunity with BH Futures Foundation",
                    BodyFirmVariant =
                        "Dear {{firmName}},\n\n" +
                        "I'm writing from BH Futures Foundation, where we support young people across Bosnia and " +
                        "Herzegovina through scholarships, mentorship and the Future Leaders Summit.\n\n" +
                        "We're looking for partners who share that commitment, and {{firmName}} stood out to us. " +
                        "Partnership can take several forms — scholarship sponsorship, mentoring, hosting a site " +
                        "visit, or speaking at one of our events.\n\n" +
                        "Would you be open to a short call to explore what might fit?\n\n" +
                        "You can read more about our work at https://www.bhfuturesfoundation.org\n\n" +
                        "Kind regards,\n" +
                        "BH Futures Foundation — Partnerships Team",

                    SubjectPersonVariant = "{{firstName}}, partnership opportunity with BH Futures Foundation",
                    BodyPersonVariant =
                        "Dear {{firstName}},\n\n" +
                        "I'm writing from BH Futures Foundation, where we support young people across Bosnia and " +
                        "Herzegovina through scholarships, mentorship and the Future Leaders Summit.\n\n" +
                        "We're looking for partners who share that commitment, and {{firmName}} stood out to us. " +
                        "Partnership can take several forms — scholarship sponsorship, mentoring, hosting a site " +
                        "visit, or speaking at one of our events.\n\n" +
                        "Would you be open to a short call to explore what might fit?\n\n" +
                        "You can read more about our work at https://www.bhfuturesfoundation.org\n\n" +
                        "Kind regards,\n" +
                        "BH Futures Foundation — Partnerships Team"
                },
                new MailingTemplate
                {
                    Name = "Law firm — pro bono and mentorship",
                    Description = "Tailored to legal practices: mentoring and pro bono support rather than cash sponsorship.",
                    FirmTypeId = lawFirmTypeId,
                    IsActive = true,
                    PersonVariantEnabled = true,

                    SubjectFirmVariant = "Mentoring law students with BH Futures Foundation",
                    BodyFirmVariant =
                        "Dear {{firmName}},\n\n" +
                        "BH Futures Foundation supports students across Bosnia and Herzegovina, and every year " +
                        "several of them are studying law.\n\n" +
                        "What they need most is not funding but access — someone to review a CV, explain how a " +
                        "practice actually works, or host a half-day visit. We're asking firms in {{city}} whether " +
                        "they would consider mentoring one student over the coming year.\n\n" +
                        "It's a small commitment with a disproportionate effect. Could we arrange a brief call?\n\n" +
                        "Kind regards,\n" +
                        "BH Futures Foundation — Partnerships Team",

                    SubjectPersonVariant = "{{firstName}}, mentoring law students with BH Futures Foundation",
                    BodyPersonVariant =
                        "Dear {{firstName}},\n\n" +
                        "BH Futures Foundation supports students across Bosnia and Herzegovina, and every year " +
                        "several of them are studying law.\n\n" +
                        "What they need most is not funding but access — someone to review a CV, explain how a " +
                        "practice actually works, or host a half-day visit. We're asking firms in {{city}} whether " +
                        "they would consider mentoring one student over the coming year.\n\n" +
                        "It's a small commitment with a disproportionate effect. Could we arrange a brief call?\n\n" +
                        "Kind regards,\n" +
                        "BH Futures Foundation — Partnerships Team"
                });

            await context.SaveChangesAsync();
        }
    }
}
