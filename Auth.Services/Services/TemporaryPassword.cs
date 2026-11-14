using System.Security.Cryptography;

namespace Auth.Services.Services
{
    public static class TemporaryPassword
    {
        /// <summary>
        /// Temporary password meeting the configured Identity policy (upper, lower, digit,
        /// symbol, 8+). Uses a cryptographic RNG rather than System.Random: these are real
        /// credentials for real accounts, and Random is seeded predictably enough that a
        /// batch generated in one run is guessable from any one of its outputs.
        ///
        /// Look-alike characters are left out because these are read aloud over the phone
        /// or copied by hand from a screen.
        /// </summary>
        public static string Generate()
        {
            const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";   // no I or O
            const string lower = "abcdefghijkmnpqrstuvwxyz";   // no l
            const string digits = "23456789";                  // no 0 or 1
            const string symbols = "!@#$%*?";

            var all = upper + lower + digits + symbols;

            var chars = new List<char>
            {
                Pick(upper), Pick(lower), Pick(digits), Pick(symbols)
            };

            while (chars.Count < 12) chars.Add(Pick(all));

            // Shuffle so the guaranteed character classes aren't always in the same positions.
            for (var i = chars.Count - 1; i > 0; i--)
            {
                var j = RandomNumberGenerator.GetInt32(i + 1);
                (chars[i], chars[j]) = (chars[j], chars[i]);
            }

            // Never start with a symbol: a leading @, + or - is read as a formula when the
            // password lands in a spreadsheet, and the CSV guard would have to alter it.
            var firstLetter = chars.FindIndex(char.IsLetterOrDigit);
            (chars[0], chars[firstLetter]) = (chars[firstLetter], chars[0]);

            return new string(chars.ToArray());

            static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
        }
    }
}
