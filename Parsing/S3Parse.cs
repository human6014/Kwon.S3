using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace KwonStudio.S3
{
    /// <summary>
    /// 셀 문자열을 값으로 바꾸는 순수 함수 모음.
    /// 숫자는 항상 InvariantCulture로 읽으므로 시트나 기기의 로케일이 달라도 결과가 같다.
    /// (한국어 로케일 기기에서 소수점이 쉼표로 해석되는 사고를 막는다.)
    /// 배열은 대괄호로 감싸 적는다. 예: <c>[0.1, 0.2]</c>, <c>[가나다라, 음음음]</c>
    /// </summary>
    public static class S3Parse
    {
        public static string String(string raw) => raw ?? string.Empty;

        public static int Int(string raw) => (int)Number(raw, "int");

        public static long Long(string raw) => (long)Number(raw, "long");

        public static float Float(string raw) => (float)Number(raw, "float");

        public static double Double(string raw) => Number(raw, "double");

        public static bool Bool(string raw)
        {
            raw = raw.Trim();
            if (raw.Length == 0) return false;

            switch (raw.ToLowerInvariant())
            {
                case "true":
                case "t":
                case "1":
                case "y":
                case "yes":
                case "o":
                    return true;
                case "false":
                case "f":
                case "0":
                case "n":
                case "no":
                case "x":
                    return false;
                default:
                    throw new S3Exception($"'{raw}' 은(는) bool로 읽을 수 없습니다. TRUE/FALSE, 1/0, O/X 중 하나를 쓰세요.");
            }
        }

        public static T Enum<T>(string raw) where T : struct, Enum
        {
            raw = raw.Trim();
            if (raw.Length == 0) return default;

            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                return (T)System.Enum.ToObject(typeof(T), number);
            }

            if (System.Enum.TryParse<T>(raw, true, out var value)) return value;

            throw new S3Exception(
                $"'{raw}' 은(는) {typeof(T).Name} 의 값이 아닙니다. 가능한 값: {string.Join(", ", System.Enum.GetNames(typeof(T)))}");
        }

        public static Vector2 Vector2(string raw)
        {
            var v = Floats(raw, 2, "Vector2");
            return new Vector2(v[0], v[1]);
        }

        public static Vector3 Vector3(string raw)
        {
            var v = Floats(raw, 3, "Vector3");
            return new Vector3(v[0], v[1], v[2]);
        }

        public static Color Color(string raw)
        {
            raw = raw.Trim();
            if (raw.Length == 0) return UnityEngine.Color.white;

            if (raw.StartsWith("#"))
            {
                if (ColorUtility.TryParseHtmlString(raw, out var html)) return html;
                throw new S3Exception($"'{raw}' 은(는) 색 코드로 읽을 수 없습니다. #RRGGBB 또는 #RRGGBBAA 형식을 쓰세요.");
            }

            var parts = raw.Trim('(', ')').Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 && parts.Length != 4)
            {
                throw new S3Exception($"Color는 '#RRGGBB' 또는 'r,g,b[,a]' 형식이어야 합니다. 받은 값: '{raw}'");
            }

            var channels = new[] { 0f, 0f, 0f, 1f };
            for (int i = 0; i < parts.Length; i++) channels[i] = (float)Number(parts[i], "float");

            // 0~255로 적은 경우를 알아서 0~1로 접어준다.
            if (channels[0] > 1f || channels[1] > 1f || channels[2] > 1f)
            {
                for (int i = 0; i < 3; i++) channels[i] /= 255f;
                if (parts.Length == 4 && channels[3] > 1f) channels[3] /= 255f;
            }

            return new Color(channels[0], channels[1], channels[2], channels[3]);
        }

        /// <summary>
        /// 배열 컬럼을 읽는다. 시트에는 <c>[0.1, 0.2]</c> 나 <c>[가나다라, 음음음]</c> 처럼 대괄호로 감싸 적는다.
        /// Vector3처럼 원소 자체에 쉼표가 들어가는 타입은 <c>[[1,2,3], [4,5,6]]</c> 로 원소마다 한 번 더 감싼다.
        /// </summary>
        public static T[] Array<T>(string raw, Func<string, T> parseElement)
        {
            raw = raw.Trim();
            if (raw.Length == 0) return System.Array.Empty<T>();

            if (raw[0] == S3Format.ArrayOpen)
            {
                if (raw[raw.Length - 1] != S3Format.ArrayClose)
                {
                    throw new S3Exception($"배열 값 '{raw}' 에 닫는 괄호 ]가 없습니다.");
                }

                var inner = raw.Substring(1, raw.Length - 2).Trim();
                if (inner.Length == 0) return System.Array.Empty<T>();

                var elements = SplitElements(inner);
                var result = new T[elements.Count];
                for (int i = 0; i < elements.Count; i++) result[i] = parseElement(elements[i].Trim());

                return result;
            }

            if (raw.IndexOf(S3Format.LegacyArraySeparator) >= 0)
            {
                throw new S3Exception($"배열은 [a, b] 형식으로 적어주세요. 받은 값: '{raw}'");
            }

            // 원소가 하나뿐이면 괄호를 생략해도 받아준다.
            return new[] { parseElement(raw) };
        }

        /// <summary>쉼표로 자르되 중첩된 대괄호 안의 쉼표는 건드리지 않는다.</summary>
        private static List<string> SplitElements(string inner)
        {
            var parts = new List<string>();
            var depth = 0;
            var start = 0;

            for (int i = 0; i < inner.Length; i++)
            {
                var c = inner[i];

                if (c == S3Format.ArrayOpen) depth++;
                else if (c == S3Format.ArrayClose) depth--;
                else if (c == S3Format.ArraySeparator && depth == 0)
                {
                    parts.Add(inner.Substring(start, i - start));
                    start = i + 1;
                }
            }

            parts.Add(inner.Substring(start));

            return parts;
        }

        private static double Number(string raw, string typeName)
        {
            raw = raw.Trim();
            if (raw.Length == 0) return 0d;

            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return value;

            // 시트에서 천 단위 쉼표나 %가 붙어 오는 경우가 잦아 한 번 더 손봐준다.
            var cleaned = raw.Replace(",", string.Empty).Replace("%", string.Empty).Trim();
            if (double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return value;

            throw new S3Exception($"'{raw}' 을(를) {typeName} 로 읽을 수 없습니다.");
        }

        private static float[] Floats(string raw, int count, string typeName)
        {
            raw = raw.Trim();
            var result = new float[count];
            if (raw.Length == 0) return result;

            var parts = raw.Trim('(', ')', '[', ']').Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != count)
            {
                throw new S3Exception($"{typeName} 은(는) 값 {count}개가 필요합니다. 받은 값: '{raw}'");
            }

            for (int i = 0; i < count; i++) result[i] = (float)Number(parts[i], "float");

            return result;
        }
    }
}
