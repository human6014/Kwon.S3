namespace KwonStudio.S3
{
    /// <summary>
    /// 배포 주소와 스프레드시트 문서들을 읽는 창구.
    /// 데이터와 enum 은 문서를 나눠 둔다. 그래야 데이터 문서의 탭 목록이 곧 테이블 목록이 되고,
    /// '문서 탭 불러오기'에 enum 탭이 섞여 들어오지 않는다.
    /// </summary>
    /// <remarks>
    /// <b>값 자체는 <see cref="S3Settings"/> 에셋에 있다.</b> 이 클래스는 읽는 문법을 그대로 두려고 남긴 창구다 —
    /// 예전엔 여기 <c>const</c> 로 박혀 있었고 호출부가 50곳쯤 되는데, 프로퍼티로 바꾸면 그 호출부가 한 줄도 안 바뀐다.
    /// 프로젝트마다 다른 값을 패키지 안에 둘 수 없어서(설치하면 읽기 전용이다) 에셋으로 뺀 것이다.
    /// </remarks>
    public static class S3Config
    {
        // ── 배포 (런타임이 읽는 곳) ──────────────────────────────────────
        // 시트는 기획자가 값을 적는 저작 도구이고, 배포된 게임은 여기 CloudFront 에서 받아온다.
        // 에디터가 시트 내용을 이 경로로 올린다. 올리는 쪽은 S3CdnPublisher 를 볼 것.

        /// <summary>배포 주소. 에디터·테섭 빌드는 테섭을, 릴리즈 빌드는 라이브를 본다.</summary>
        /// <remarks>
        /// UNITY_EDITOR 를 함께 보는 이유: TEST_SERVER 는 Android 에만 정의돼 있어서
        /// 이게 없으면 에디터에서 플레이할 때 라이브 데이터를 받게 된다.
        /// 공용 환경 스위치가 생기면 이 분기를 그쪽으로 옮긴다. 지금은 소비자가 S3 뿐이라 여기 둔다.
        /// </remarks>
        public static string BaseUrl =>
#if UNITY_EDITOR || TEST_SERVER
            S3Settings.Instance != null ? S3Settings.Instance.testBaseUrl : string.Empty;
#else
            S3Settings.Instance != null ? S3Settings.Instance.liveBaseUrl : string.Empty;
#endif

        /// <summary>
        /// 배포 데이터가 놓이는 경로. 끝의 버전은 서식 규약이 구 빌드와 호환되지 않게 바뀔 때를 위한 탈출구다.
        /// 이미 배포된 빌드는 자기가 아는 경로만 보므로, 서식을 갈아엎을 때 v2 로 올리고 v1 을 남겨두면 된다.
        /// </summary>
        public static string RemotePrefix =>
            S3Settings.Instance != null ? S3Settings.Instance.remotePrefix : string.Empty;

        /// <summary>모든 테이블을 담은 번들 파일 이름.</summary>
        public const string BundleFileName = "tables.json";

        /// <summary>다운로드 타임아웃(초). 모바일 회선을 감안해 넉넉히 잡았다.</summary>
        public const int TimeoutSeconds = 20;

#if UNITY_EDITOR
        /// <summary>
        /// 에디터 오프라인 모드가 대신 읽는 로컬 번들.
        /// <c>Library</c> 아래라 git 에도 빌드에도 들어가지 않는다 — 두 번째 데이터 출처가 되지 않게 하려는 것이다.
        /// 만드는 쪽은 <c>S3OfflineBundle</c>(에디터), 읽는 쪽은 프로젝트의 오프라인 데코레이터.
        /// </summary>
        public const string OfflineBundlePath = "Library/S3Offline/" + BundleFileName;
#endif

        /// <summary>
        /// 테이블 전부를 한 번에 받아오는 주소.
        /// 테이블마다 따로 받지 않는 이유는 요청 수보다 <b>결과가 원자적</b>이기 때문이다 —
        /// 폴백이 없어서, 일부만 받아진 상태는 어차피 게임을 진행시키지 못한다.
        /// </summary>
        public static string BuildBundleUrl() => $"{BaseUrl}/{RemotePrefix}/{BundleFileName}";

        // ── 저작 (에디터가 읽는 곳) ──────────────────────────────────────

        /// <summary>기획 데이터 문서 ID. 탭 하나 = 테이블 하나.</summary>
        public static string SpreadsheetId =>
            S3Settings.Instance != null ? S3Settings.Instance.spreadsheetId : string.Empty;

        /// <summary>열거형 문서 ID. 탭 하나 = enum 하나.</summary>
        public static string EnumSpreadsheetId =>
            S3Settings.Instance != null ? S3Settings.Instance.enumSpreadsheetId : string.Empty;

        /// <summary>
        /// 로컬라이징 문서 ID. 탭 하나 = 테이블 하나로 데이터 문서와 규칙이 같고, 문서만 다르다.
        /// 번역을 맡기는 사람에게 기획 수치까지 열어주지 않으려고 나눠 둔다.
        /// </summary>
        public static string LocalizationSpreadsheetId =>
            S3Settings.Instance != null ? S3Settings.Instance.localizationSpreadsheetId : string.Empty;

        /// <summary>
        /// 테이블이 들어 있는 문서 전부. 시트 항목의 문서가 비어 있으면 <b>첫 번째</b>를 쓴다.
        /// 탭 목록 조회·미등록 탭 감지가 이 목록을 훑으므로, 문서를 늘리려면 여기에만 더하면 된다.
        /// </summary>
        public static string[] TableDocumentIds => new[] { SpreadsheetId, LocalizationSpreadsheetId };

        /// <summary>탭 하나를 CSV로 받아오는 주소.</summary>
        public static string CsvUrl(string documentId, string gid) =>
            $"https://docs.google.com/spreadsheets/d/{documentId}/export?format=csv&gid={NormalizeGid(gid)}";

        /// <summary>브라우저에서 해당 탭을 여는 주소.</summary>
        public static string EditUrl(string documentId, string gid) =>
            $"https://docs.google.com/spreadsheets/d/{documentId}/edit#gid={NormalizeGid(gid)}";

        /// <summary>
        /// 문서 전체를 HTML로 보는 주소. 이 페이지에는 모든 탭의 이름과 gid가 들어 있어
        /// API 키 없이 탭 목록을 알아낼 수 있다.
        /// </summary>
        public static string HtmlViewUrl(string documentId) =>
            $"https://docs.google.com/spreadsheets/d/{documentId}/htmlview";

        /// <summary>데이터 문서의 탭 CSV 주소.</summary>
        public static string BuildCsvUrl(string gid) => CsvUrl(SpreadsheetId, gid);

        /// <summary>데이터 문서의 탭 편집 주소.</summary>
        public static string BuildEditUrl(string gid) => EditUrl(SpreadsheetId, gid);

        private static string NormalizeGid(string gid) =>
            string.IsNullOrWhiteSpace(gid) ? "0" : gid.Trim();
    }
}
