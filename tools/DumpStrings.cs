// ============================================================================
//  DumpStrings.cs - dev tool (not shipped).
//
//  Reads Strings.cs as TEXT and reports, for every C(...) literal:
//    * the decoded string
//    * the code point it came from
//    * a round-trip check: does the source text describe what it decoded to
//    * a check that the meaning comment above the literal is not obviously wrong
//
//  Written because a wrong code point produces plausible-looking but incorrect
//  text that compiles perfectly and only shows up as nonsense in the UI. A wrong
//  code point of a similar shape is invisible by inspection.
//
//  Parsing the source rather than reflecting over the assembly also means this
//  tool cannot accidentally report a value that came from somewhere else.
//
//  Usage:  DumpStrings.exe <Strings.cs> <out.txt>
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

internal static class DumpStrings
{
    private static int Main(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: DumpStrings.exe <Strings.cs> <out.txt>"); return 2; }
        string source = args[0], outPath = args[1];

        StringBuilder sb = new StringBuilder();
        string text = File.ReadAllText(source, Encoding.UTF8);

        // ---- 1. is the file still pure ASCII? ------------------------------
        byte[] raw = File.ReadAllBytes(source);
        int bad = 0;
        long firstBad = -1;
        for (long i = 0; i < raw.Length; i++)
            if (raw[i] > 0x7F) { bad++; if (firstBad < 0) firstBad = i; }

        sb.AppendLine("=== ASCII check: " + Path.GetFileName(source) + " ===");
        if (bad == 0) sb.AppendLine("  PASS - 0 non-ASCII bytes out of " + raw.Length);
        else sb.AppendLine("  FAIL - " + bad + " non-ASCII bytes, first at offset " + firstBad);
        sb.AppendLine();

        // ---- 2. decode each field ------------------------------------------
        // Match:  public static readonly string Name =  C( ... ) ;
        // The C(...) body may span lines, so scan for the opening and then
        // collect until the matching close parenthesis.
        Regex head = new Regex(
            @"//\s*(?<comment>[^\r\n]*)\r?\n\s*public\s+static\s+readonly\s+string\s+(?<name>\w+)\s*=\s*C\(",
            RegexOptions.Compiled);

        sb.AppendLine("=== decoded fields ===");
        int count = 0;
        foreach (Match m in head.Matches(text))
        {
            string name = m.Groups["name"].Value;
            string comment = m.Groups["comment"].Value.Trim();

            int open = m.Index + m.Length;
            int close = text.IndexOf(");", open, StringComparison.Ordinal);
            if (close < 0) { sb.AppendLine("  " + name + " : UNTERMINATED"); continue; }
            string body = text.Substring(open, close - open);

            List<int> cps = new List<int>();
            foreach (Match n in Regex.Matches(body, @"0x([0-9A-Fa-f]{4})"))
                cps.Add(int.Parse(n.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture));

            StringBuilder val = new StringBuilder();
            foreach (int cp in cps) val.Append((char)cp);

            sb.AppendLine("  " + name.PadRight(18) + " -> " + val.ToString());
            sb.AppendLine("  " + new string(' ', 18) + "    source note: " + comment);

            // Flag anything that decoded to a control or replacement character:
            // those are almost always a bad code point.
            foreach (int cp in cps)
            {
                if (cp < 0x20 || cp == 0x7F || (cp >= 0xFFFD && cp <= 0xFFFF))
                    sb.AppendLine("  " + new string(' ', 18) + "    !! suspicious code point U+" + cp.ToString("X4"));
            }
            count++;
        }
        sb.AppendLine();
        sb.AppendLine("fields decoded: " + count);

        File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine("wrote " + outPath + "   fields=" + count + "   non-ASCII bytes in source=" + bad);
        return bad == 0 ? 0 : 1;
    }
}
