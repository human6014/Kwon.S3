using UnityEngine;

namespace KwonStudio.S3
{
    /// <summary>
    /// 프로젝트마다 다른 값 — 배포 주소와 스프레드시트 문서 ID.
    /// </summary>
    /// <remarks>
    /// <b>패키지가 아니라 프로젝트가 들고 있어야 하는 값이라 에셋으로 뺐다.</b>
    /// 패키지는 git·registry 로 설치하면 <c>Library/PackageCache</c> 에 읽기 전용으로 들어가서
    /// 안에 상수로 박아두면 프로젝트마다 다른 값을 줄 수 없다.
    /// <para>
    /// <b><c>Resources</c> 아래에 두는 이유:</b> 이 값을 런타임(<see cref="S3Remote"/>)도 읽는다.
    /// 에디터라면 타입 검색으로 찾겠지만 빌드본에는 AssetDatabase 가 없다.
    /// </para>
    /// </remarks>
    public class S3Settings : ScriptableObject
    {
        /// <summary><see cref="Resources.Load"/> 에 넘기는 경로. 실물은 <c>Assets/Resources/S3/S3Settings.asset</c>.</summary>
        public const string ResourcePath = "S3/S3Settings";

        [Header("배포 (런타임이 읽는 곳)")]
        [Tooltip("테섭 CloudFront 배포 주소. 에디터와 TEST_SERVER 빌드가 여기를 본다.")]
        public string testBaseUrl = "";

        [Tooltip("라이브 CloudFront 배포 주소. 릴리즈 빌드가 여기를 본다.")]
        public string liveBaseUrl = "";

        [Tooltip("배포 데이터가 놓이는 경로. 서식 규약이 구 빌드와 호환되지 않게 바뀔 때 v2, v3 으로 올린다.")]
        public string remotePrefix = "config/v1";

        [Header("저작 (에디터가 읽는 곳)")]
        [Tooltip("기획 데이터 문서 ID. 탭 하나 = 테이블 하나.")]
        public string spreadsheetId = "";

        [Tooltip("열거형 문서 ID. 탭 하나 = enum 하나.")]
        public string enumSpreadsheetId = "";

        [Tooltip("로컬라이징 문서 ID. 데이터 문서와 규칙은 같고 문서만 다르다.")]
        public string localizationSpreadsheetId = "";

        private static S3Settings _instance;
        private static bool _isMissingReported;

        /// <summary>
        /// 설정 에셋. 없으면 <c>null</c> 을 돌려주고 에러를 한 번만 남긴다.
        /// </summary>
        /// <remarks>
        /// 없으면 조용히 기본값으로 도는 게 아니라 데이터 로드가 실패해야 한다 — 이 파이프라인은
        /// 원래 폴백이 없고, 잘못된 주소로 조용히 도는 쪽이 훨씬 찾기 어렵다.
        /// </remarks>
        public static S3Settings Instance
        {
            get
            {
                if (_instance != null) return _instance;

                _instance = Resources.Load<S3Settings>(ResourcePath);

                if (_instance == null && !_isMissingReported)
                {
                    _isMissingReported = true;
                    Debug.LogError(
                        $"[S3] 설정 에셋이 없습니다. Assets/Resources/{ResourcePath}.asset 을 만들어 " +
                        "배포 주소와 문서 ID를 채워주세요. (Tools/KwonStudio/S3 임포터 에서 만들 수 있습니다.)");
                }

                return _instance;
            }
        }

#if UNITY_EDITOR
        /// <summary>기본 폴더. 처음 만들 때 놓을 자리일 뿐이고, 옮겨도 <c>Resources</c> 아래이기만 하면 된다.</summary>
        public const string DefaultFolder = "Assets/Resources/S3";

        public const string DefaultAssetPath = DefaultFolder + "/S3Settings.asset";

        /// <summary>설정 에셋을 찾고, 없으면 기본 경로에 새로 만든다. 에디터 전용.</summary>
        public static S3Settings GetOrCreate()
        {
            var settings = Resources.Load<S3Settings>(ResourcePath);
            if (settings != null) return _instance = settings;

            settings = CreateInstance<S3Settings>();

            // S3Paths.EnsureAssetFolder 를 쓰지 않는다 — 그건 에디터 어셈블리에 있고 이 파일은 런타임 쪽이라
            // 참조 방향이 거꾸로다. CreateAsset 은 폴더가 AssetDatabase 에 등록돼 있어야 하므로 Refresh 로 알린다.
            System.IO.Directory.CreateDirectory(DefaultFolder);
            UnityEditor.AssetDatabase.Refresh();

            UnityEditor.AssetDatabase.CreateAsset(settings, DefaultAssetPath);
            UnityEditor.AssetDatabase.SaveAssets();
            Debug.Log($"[S3] 설정 에셋을 새로 만들었습니다: {DefaultAssetPath}");

            _isMissingReported = false;

            return _instance = settings;
        }
#endif
    }
}
