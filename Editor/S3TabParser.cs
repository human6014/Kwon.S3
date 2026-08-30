using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KwonStudio.S3.EditorTools
{
    /// <summary>문서에 들어 있는 탭 하나.</summary>
    public readonly struct S3TabInfo
    {
        public readonly string Name;
        public readonly string Gid;

        public S3TabInfo(string name, string gid)
        {
            Name = name;
            Gid = gid;
        }

        public override string ToString() => $"{Name} (gid {Gid})";
    }

    /// <summary>
    /// htmlview 페이지에서 탭 목록을 뽑아낸다. 네트워크와 무관한 순수 파싱이라 따로 떼어 뒀다.
    /// 받아오는 쪽은 <see cref="S3SheetIndex"/>다.
    /// </summary>
    public static class S3TabParser
    {
        // htmlview 안에 이런 모양으로 들어 있다.
        //   items.push({name: "Test2", pageUrl: "...gid=1370807956", gid: "1370807956", ...});
        private static readonly Regex TabPattern = new Regex(
            "items\\.push\\(\\{name:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"[^}]*?gid:\\s*\"(\\d+)\"",
            RegexOptions.Compiled);

        public static List<S3TabInfo> Parse(string html)
        {
            var tabs = new List<S3TabInfo>();
            var seen = new HashSet<string>();

            foreach (Match match in TabPattern.Matches(html))
            {
                var gid = match.Groups[2].Value;
                if (!seen.Add(gid)) continue;

                tabs.Add(new S3TabInfo(Unescape(match.Groups[1].Value), gid));
            }

            if (tabs.Count == 0)
            {
                throw new S3Exception(
                    "문서에서 탭 목록을 읽지 못했습니다. 공유 설정이 '링크가 있는 모든 사용자'인지 확인하세요. " +
                    "구글이 페이지 구조를 바꾼 경우에도 이 오류가 납니다.");
            }

            return tabs;
        }

        /// <summary>탭 이름을 클래스 이름으로 쓸 수 있게 다듬는다.</summary>
        public static string ToTableName(string tabName)
        {
            var sb = new StringBuilder(tabName.Length);
            var upperNext = true;

            foreach (var c in tabName)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    sb.Append(upperNext ? char.ToUpperInvariant(c) : c);
                    upperNext = false;
                }
                else
                {
                    // 공백이나 기호는 버리고 다음 글자를 대문자로 이어 붙인다. ("몬스터 정보" → "몬스터정보")
                    upperNext = true;
                }
            }

            if (sb.Length == 0) return "Sheet";
            if (char.IsDigit(sb[0])) sb.Insert(0, '_');

            return sb.ToString();
        }

        /// <summary>JS 문자열 이스케이프를 되돌린다. 탭 이름에 따옴표나 \x 이스케이프가 들어올 수 있다.</summary>
        public static string Unescape(string raw)
        {
            if (raw.IndexOf('\\') < 0) return raw;

            var sb = new StringBuilder(raw.Length);

            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] != '\\' || i + 1 >= raw.Length)
                {
                    sb.Append(raw[i]);
                    continue;
                }

                var next = raw[++i];
                switch (next)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'x': i += AppendCodePoint(sb, raw, i + 1, 2); break;
                    case 'u': i += AppendCodePoint(sb, raw, i + 1, 4); break;
                    default: sb.Append(next); break; // \" \\ \/ 등
                }
            }

            return sb.ToString();
        }

        /// <summary>16진수 이스케이프를 읽어 붙이고, 소비한 글자 수를 돌려준다.</summary>
        private static int AppendCodePoint(StringBuilder sb, string raw, int start, int length)
        {
            if (start + length > raw.Length) return 0;

            var hex = raw.Substring(start, length);
            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code)) return 0;

            sb.Append((char)code);
            return length;
        }
    }
}
