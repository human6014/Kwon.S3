using System;
using UnityEngine;

namespace KwonStudio.S3
{
    /// <summary>번들에 담긴 테이블 하나. 원본 CSV 와 그걸 읽을 좌표를 함께 나른다.</summary>
    [Serializable]
    public class S3BundleEntry
    {
        /// <summary>테이블 클래스 이름. 예: MonsterDataTable</summary>
        public string name;

        /// <summary>필드명이 적힌 행 번호(시트에 보이는 그대로 1부터).</summary>
        public int headerRow;

        /// <summary>데이터가 시작되는 열(A, B, AA …).</summary>
        public string startColumn;

        /// <summary>시트에서 받은 CSV 본문.</summary>
        public string csv;

        /// <summary>
        /// 좌표를 읽기 형태로 바꾼다.
        /// <see cref="S3Layout"/> 을 그대로 직렬화하지 않는 이유는 그쪽 필드가 private(<c>_headerRow</c>)이라
        /// 내부 이름이 전송 형식에 새기 때문이다. 여기서는 평평한 두 값만 주고받는다.
        /// </summary>
        public S3Layout ToLayout() => new S3Layout(headerRow, startColumn);
    }

    /// <summary>
    /// 모든 테이블을 담은 배포 단위. 요청 한 번으로 전부 받기 위해 하나로 묶는다.
    /// </summary>
    /// <remarks>
    /// <b>내용이 같으면 바이트도 같아야 한다.</b> 에디터의 'AWS 상태 보기'가 ETag(본문 MD5)와
    /// 새로 만든 번들의 MD5 를 맞춰보기 때문이다. 그래서 빌드 시각 같은 값을 넣지 않고,
    /// 항목 순서도 임포터 설정 순서로 고정한다.
    /// </remarks>
    [Serializable]
    public class S3Bundle
    {
        /// <summary>번들 형식 버전. 지금 형식은 2 — 항목마다 읽기 좌표가 함께 들어간다.</summary>
        public const string CurrentVersion = "2";

        public string version;

        public S3BundleEntry[] tables;

        /// <summary>
        /// 받은 본문을 번들로 읽는다. 200 인데 HTML·XML 이 온 경우도 여기서 걸린다.
        /// </summary>
        /// <param name="error">실패 사유. 성공하면 null.</param>
        public static bool TryParse(string json, out S3Bundle bundle, out string error)
        {
            bundle = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "받은 내용이 비어 있습니다.";
                return false;
            }

            try
            {
                bundle = JsonUtility.FromJson<S3Bundle>(json);
            }
            catch (Exception e)
            {
                bundle = null;
                error = $"번들을 읽지 못했습니다: {e.Message} 앞부분: {Head(json)}";
                return false;
            }

            if (bundle?.tables == null)
            {
                // JsonUtility 는 형식이 달라도 예외 없이 빈 객체를 돌려주는 경우가 있어 여기서 한 번 더 본다.
                bundle = null;
                error = $"번들 형식이 아닙니다. 앞부분: {Head(json)}";
                return false;
            }

            if (bundle.version != CurrentVersion)
            {
                var found = string.IsNullOrEmpty(bundle.version) ? "(없음)" : bundle.version;
                bundle = null;
                error = $"번들 버전이 맞지 않습니다. 이 빌드는 {CurrentVersion} 을 읽습니다. 받은 값: {found}";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>이름으로 항목을 찾는다. 없으면 null.</summary>
        public S3BundleEntry Find(string tableName)
        {
            if (tables == null) return null;

            for (int i = 0; i < tables.Length; i++)
            {
                var entry = tables[i];
                if (entry != null && entry.name == tableName) return entry;
            }

            return null;
        }

        /// <summary>실기기 로그만 보고 원인을 짚을 수 있도록 응답 앞부분을 한 줄로 만든다.</summary>
        private static string Head(string text)
        {
            if (string.IsNullOrEmpty(text)) return "(빈 응답)";

            var head = text.TrimStart().Replace('\n', ' ').Replace('\r', ' ');
            return head.Length <= 40 ? head : head.Substring(0, 40) + "...";
        }
    }
}
