// ============================================================================
//  Strings.cs - every non-ASCII label used by the UI, built from code points.
//
//  Why this file exists
//  --------------------
//  Windows PowerShell 5.1 decodes a BOM-less file as ANSI, and the C# compiler
//  can likewise be handed an encoding it reads differently. An earlier revision
//  of this launcher ended up permanently blind to the Multisim error dialog for
//  exactly that reason: its Chinese match literal had been corrupted on disk.
//
//  Keeping every .cs file in this project pure ASCII removes that whole class of
//  failure. CJK text is assembled from code points, which is immune to how the
//  file is decoded.
//
//  Rule for this file: NOT ONE non-ASCII BYTE, not even in a comment. Spell the
//  meaning out in English instead of pasting the characters - otherwise the
//  "pure ASCII" guarantee is a lie.
//
//  Every code point below was verified by decoding it back and reading the
//  result. If you add a string, do the same: a single wrong code point yields
//  plausible-looking wrong text that no compiler will complain about.
// ============================================================================

namespace MultisimLauncher
{
    internal static class Strings
    {
        /// <summary>Build a string from Unicode code points.</summary>
        private static string C(params int[] codePoints)
        {
            char[] chars = new char[codePoints.Length];
            for (int i = 0; i < codePoints.Length; i++) chars[i] = (char)codePoints[i];
            return new string(chars);
        }

        // "keeps retrying until the component library loads"
        public static readonly string Tagline =
            C(0x4F1A, 0x4E00, 0x76F4, 0x91CD, 0x8BD5, 0xFF0C, 0x76F4, 0x5230,
              0x5143, 0x5668, 0x4EF6, 0x5E93, 0x52A0, 0x8F7D, 0x6210, 0x529F);

        // ---- sidebar section titles ---------------------------------------
        // "status"
        public static readonly string Status = C(0x72B6, 0x6001);
        // "general"
        public static readonly string General = C(0x901A, 0x7528);

        // ---- sidebar items -------------------------------------------------
        // "show details"
        public static readonly string ShowDetails =
            C(0x663E, 0x793A, 0x8BE6, 0x7EC6, 0x4FE1, 0x606F);
        // "open log"
        public static readonly string OpenLog = C(0x6253, 0x5F00, 0x65E5, 0x5FD7);
        // "quit"
        public static readonly string Quit = C(0x9000, 0x51FA);

        // ---- states --------------------------------------------------------
        // "ready"
        public static readonly string Ready = C(0x5C31, 0x7EEA);
        // "starting"
        public static readonly string Starting = C(0x542F, 0x52A8, 0x4E2D);
        // "running"
        public static readonly string Running = C(0x8FD0, 0x884C, 0x4E2D);
        // "closing"
        public static readonly string Stopping = C(0x6B63, 0x5728, 0x5173, 0x95ED);

        // ---- buttons -------------------------------------------------------
        // "start"
        public static readonly string Start = C(0x542F, 0x52A8);
        // "stop"
        public static readonly string Stop = C(0x505C, 0x6B62);

        // ---- detail lines --------------------------------------------------
        // "no."          (prefix of "attempt N")
        public static readonly string AttemptPrefix = C(0x7B2C);
        // "attempt"      (suffix of "attempt N")
        public static readonly string AttemptSuffix = C(0x6B21, 0x5C1D, 0x8BD5);
        // "worked on the first try"
        public static readonly string FirstTryOk =
            C(0x7B2C, 0x4E00, 0x6B21, 0x5C31, 0x6210, 0x529F);
        // "gave up"
        public static readonly string GaveUp = C(0x5DF2, 0x653E, 0x5F03);
        // "install first"
        public static readonly string InstallFirst = C(0x8BF7, 0x5148, 0x5B89, 0x88C5);
        // "not found"
        public static readonly string NotFound = C(0x672A, 0x627E, 0x5230);
        // "master database"
        public static readonly string MasterDb = C(0x4E3B, 0x6570, 0x636E, 0x5E93);
        // "corporate database"
        public static readonly string CorporateDb =
            C(0x4F01, 0x4E1A, 0x6570, 0x636E, 0x5E93);
        // "loaded"
        public static readonly string Loaded = C(0x5DF2, 0x52A0, 0x8F7D);
        // "not loaded"
        public static readonly string NotLoaded = C(0x672A, 0x52A0, 0x8F7D);

        // ---- silent start --------------------------------------------------
        // "silent start"
        public static readonly string HiddenStart = C(0x9759, 0x9ED8, 0x542F, 0x52A8);
        // "only show the window once the component library has loaded"
        public static readonly string HiddenHint =
            C(0x5143, 0x5668, 0x4EF6, 0x5E93, 0x52A0, 0x8F7D, 0x597D,
              0x4E86, 0x624D, 0x663E, 0x793A, 0x7A97, 0x53E3);
    }
}
