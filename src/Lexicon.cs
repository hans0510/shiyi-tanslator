using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Shiyi
{
    internal sealed class LookupQuery
    {
        private static readonly Regex Token = new Regex(@"^[\p{L}\p{Nd}][\p{L}\p{M}\p{Nd}]*(?:['’\-‐‑·][\p{L}\p{Nd}][\p{L}\p{M}\p{Nd}]*)*$", RegexOptions.CultureInvariant);
        private static readonly Regex Initials = new Regex(@"^(?:\p{L}\.){2,}\p{L}?$");
        internal static readonly Regex Title = new Regex(@"^(?:Mr|Mrs|Ms|Dr|Prof|Sr|Jr)\.$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Unsegmented = new Regex(@"[\u3040-\u30ff\u3400-\u9fff\uf900-\ufaff\uac00-\ud7af\u0e00-\u0eff\u1000-\u109f\u1780-\u17ff]");
        public readonly string Source, Word;
        public readonly bool MayBeSentence;
        public Task<SentenceContext> Context { get; internal set; }

        public LookupQuery(string source)
        {
            Source = source ?? "";
            Context = Task.FromResult<SentenceContext>(null);
            string trimmed = Source.Trim();
            if (trimmed.Length == 0 || trimmed.Length > 100) return;
            string candidate = trimmed.Trim(" \t\"'“”‘’«»‹›()[]{}（）「」『』【】,，、;；:：".ToCharArray());
            if (!candidate.Any(char.IsLetter) || (!Token.IsMatch(candidate) && !Initials.IsMatch(candidate) && !Title.IsMatch(candidate))) return;
            Word = candidate;
            MayBeSentence = Unsegmented.IsMatch(candidate);
        }
    }

    internal sealed class SentenceContext
    {
        internal const int SideLimit = 300;
        internal readonly string Before, After;

        private SentenceContext(string before, string after) { Before = before; After = after; }

        // The selection offset comes from the accessibility range, even if the same word occurs twice.
        internal static SentenceContext Extract(string left, string selected, string right, string word)
        {
            int offset = selected.IndexOf(word, StringComparison.Ordinal);
            if (offset < 0 || left.Length > SideLimit || right.Length > SideLimit) return null;
            string text = left + selected + right;
            int start = left.Length + offset, end = start + word.Length;
            int a = start, b = end;
            while (a > 0 && !Boundary(text, a - 1)) a--;
            if (a > 0)
            {
                int close = a;
                while (close < start && "\"”’»」』)]".IndexOf(text[close]) >= 0) close++;
                if (close < start && char.IsWhiteSpace(text[close])) a = close;
            }
            while (b < text.Length && !Boundary(text, b)) b++;
            if (b < text.Length && text[b] != '\r' && text[b] != '\n')
            {
                b++;
                while (b < text.Length && "!?！？。…\"”’»」』".IndexOf(text[b]) >= 0) b++;
            }
            string before = text.Substring(a, start - a).TrimStart(), after = text.Substring(end, b - end).TrimEnd();
            if (!before.Any(char.IsLetterOrDigit) && !after.Any(char.IsLetterOrDigit)) return null;
            return new SentenceContext(before, after);
        }

        private static bool Boundary(string text, int at)
        {
            char c = text[at];
            if ("\r\n!?。！？…".IndexOf(c) >= 0) return true;
            if (c != '.') return false;
            if (at > 0 && at + 1 < text.Length && char.IsLetterOrDigit(text[at - 1]) && char.IsLetterOrDigit(text[at + 1])) return false;
            int first = at;
            while (first > 0 && (char.IsLetter(text[first - 1]) || text[first - 1] == '.')) first--;
            string token = text.Substring(first, at - first).ToLowerInvariant();
            if (LookupQuery.Title.IsMatch(token + ".") || token == "e.g" || token == "i.e") return false;
            if (Regex.IsMatch(token, @"^\p{L}(?:\.\p{L})+$"))
            {
                int next = at + 1;
                while (next < text.Length && (text[next] == ' ' || text[next] == '\t')) next++;
                return next >= text.Length || !char.IsLower(text[next]);
            }
            return true;
        }
    }

    internal sealed class DictionarySense
    {
        public string Pos { get; set; }
        public string Meaning { get; set; }
    }

    internal sealed class BilingualExample
    {
        public string Source { get; set; }
        public string Translation { get; set; }
    }

    internal sealed class WordEntry
    {
        internal string Word, Lemma, Language, Ipa, ContextMeaning;
        internal SentenceContext Context;
        internal DictionarySense[] Senses;
        internal string[] Collocations;
        internal BilingualExample Example;

        internal string AsText()
        {
            var body = new StringBuilder(Word);
            if (Ipa.Length > 0) body.Append("  /").Append(Ipa).Append("/");
            if (Lemma.Length > 0) body.AppendLine().Append("原形  ").Append(Lemma);
            if (Context != null)
            {
                body.AppendLine().AppendLine().AppendLine("句中义").AppendLine(ContextMeaning);
                body.Append(Context.Before).Append(Word).Append(Context.After);
            }
            body.AppendLine().AppendLine().Append("常见释义");
            foreach (var sense in Senses) body.AppendLine().Append((sense.Pos + "  " + sense.Meaning).Trim());
            if (Collocations.Length > 0) body.AppendLine().AppendLine().AppendLine("常用搭配").Append(string.Join(Environment.NewLine, Collocations));
            if (Example != null)
            {
                body.AppendLine().AppendLine().AppendLine("例句").Append(Example.Source);
                if (Example.Translation.Length > 0) body.AppendLine().Append(Example.Translation);
            }
            return body.ToString();
        }
    }

    internal sealed class LookupResult
    {
        internal readonly string Text;
        internal readonly WordEntry Entry;
        internal LookupResult(string translation) { Text = translation; }
        private LookupResult(WordEntry entry) { Entry = entry; Text = entry.AsText(); }

        private sealed class Reply
        {
            public string kind { get; set; }
            public string translation { get; set; }
            public string language { get; set; }
            public string lemma { get; set; }
            public string ipa { get; set; }
            public string contextMeaning { get; set; }
            public DictionarySense[] senses { get; set; }
            public string[] collocations { get; set; }
            public BilingualExample example { get; set; }
        }

        private static string Clean(string s) { return (s ?? "").Trim(); }

        internal static LookupResult Parse(string content, LookupQuery query, SentenceContext context, JavaScriptSerializer json)
        {
            // Accept bare JSON and JSON inside a Markdown fence from compatible local models.
            int start = content.IndexOf('{'), end = content.LastIndexOf('}');
            Reply reply = null;
            if (start >= 0 && end > start)
            {
                try { reply = json.Deserialize<Reply>(content.Substring(start, end - start + 1)); }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
                catch (FormatException) { }
            }
            if (reply == null) throw InvalidEntry();
            if (reply.kind == "translation" && query.MayBeSentence && !string.IsNullOrWhiteSpace(reply.translation))
                return new LookupResult(reply.translation.Trim());
            if (reply.kind == "unknown") throw new TranslationException("模型未能确认这个词的释义，请检查拼写或扩大选区。");
            if (reply.kind != "word" || reply.senses == null) throw InvalidEntry();
            var senses = reply.senses.Where(s => s != null && !string.IsNullOrWhiteSpace(s.Meaning)).Take(8)
                .Select(s => new DictionarySense { Pos = Clean(s.Pos), Meaning = Clean(s.Meaning) }).ToArray();
            if (senses.Length == 0) throw InvalidEntry();
            string lemma = Clean(reply.lemma);
            var entry = new WordEntry {
                Word = query.Word, Lemma = string.Equals(query.Word, lemma, StringComparison.OrdinalIgnoreCase) ? "" : lemma,
                Language = Clean(reply.language), Ipa = Clean(reply.ipa).Trim('/', '[', ']').Trim(),
                Context = context, ContextMeaning = context == null ? "" : Clean(reply.contextMeaning),
                Senses = senses, Collocations = (reply.collocations ?? new string[0]).Select(Clean).Where(s => s.Length > 0).Distinct().Take(3).ToArray()
            };
            if (context != null && entry.ContextMeaning.Length == 0) entry.ContextMeaning = "词条未给出明确的句中义。";
            if (reply.example != null && !string.IsNullOrWhiteSpace(reply.example.Source))
                entry.Example = new BilingualExample { Source = Clean(reply.example.Source), Translation = Clean(reply.example.Translation) };
            return new LookupResult(entry);
        }

        private static TranslationException InvalidEntry()
        { return new TranslationException("模型未返回可用的词典条目，请重试或在模型设置中换一个模型。"); }
    }
}
