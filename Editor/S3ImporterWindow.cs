using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace KwonStudio.S3.EditorTools
{
    /// <summary>
    /// 구글 스프레드시트를 테이블 에셋으로 굽는 창. <c>TD/데이터 도구</c> 의 S3 탭이다.
    /// </summary>
    public class S3ImporterWindow : EditorWindow
    {
        private const string LayoutHelp =
            "시작 행/열이 가리키는 칸부터 아래처럼 세 덩어리로 읽습니다. (아래는 시작이 A1인 경우)\n" +
            "\n" +
            "  1행  필드명   id        name      hp     atk speed   tags        #memo\n" +
            "  2행  타입     int       string    int    float       string[]    string\n" +
            "  3행~ 데이터   1         고블린     30     1.2         fast|weak   아무거나\n" +
            "               2         오크       80     0.8         tank\n" +
            "               #3        임시 데이터 (키 칸이 #이면 통째로 무시)\n" +
            "\n" +
            "위 시트는 이런 코드가 됩니다.\n" +
            "  MonsterData      : id, name, hp, atkSpeed, tags 필드를 가진 [Serializable] 클래스\n" +
            "  MonsterDataTable : 위 데이터를 담는 ScriptableObject (Get(1) 로 조회)\n" +
            "\n" +
            "필드명 규칙\n" +
            "  · 공백과 기호는 지워지고 다음 글자가 대문자가 됩니다. 'atk speed' → atkSpeed\n" +
            "  · 한글 헤더도 되지만 필드명이 한글이 되므로 영문 권장\n" +
            "  · 숫자로 시작하면 앞에 _가 붙습니다. C# 예약어면 @가 붙습니다\n" +
            "  · 필드명이 서로 겹치면 임포트가 중단됩니다 ('atk speed'와 'atkSpeed'는 같은 이름이 됩니다)";

        private const string RegionHelp =
            "시트에 데이터만 있진 않으므로, 데이터 블록의 왼쪽 위 모서리를 지정할 수 있습니다.\n" +
            "\n" +
            "  시작 행 = 필드명이 적힌 행 번호 (시트에 보이는 그대로 1부터)\n" +
            "  시작 열 = 데이터가 시작되는 열 (A, B, AA … 또는 1, 2 … 로 적어도 됩니다)\n" +
            "\n" +
            "시작을 C5로 잡으면 이렇게 읽습니다.\n" +
            "\n" +
            "        A          B        C         D        E\n" +
            "   1  [기획 메모 영역 — 아예 보지 않음]\n" +
            "   2\n" +
            "   3\n" +
            "   4\n" +
            "   5  메모        메모      id        name     hp        ← 필드명 (C열부터)\n" +
            "   6                       int       string   int       ← 타입\n" +
            "   7                       1         고블린    30        ← 데이터\n" +
            "   8                       2         오크      80\n" +
            "   9  ※ 밸런스 재검토 필요        ← 키 칸(C열)이 비어서 저절로 무시됨\n" +
            "\n" +
            "메모가 걸러지는 방식은 세 가지입니다.\n" +
            "  · 시작 행/열보다 위나 왼쪽 → 아예 읽지 않음\n" +
            "  · 데이터 블록 오른쪽 컬럼 → 필드명이나 타입 칸을 비워두면 무시 (또는 필드명 앞에 #)\n" +
            "  · 데이터 블록 아래/사이의 줄 → 키 컬럼 칸이 비어 있으면 무시\n" +
            "    키 칸에 글자를 적어야 하는 메모라면 앞에 #을 붙이세요\n" +
            "\n" +
            "주의: 키 컬럼 칸이 빈 행은 데이터로 치지 않습니다. 키가 없는 행은 애초에 조회할 수 없으니\n" +
            "실수로 빠뜨린 키도 여기서 같이 걸러집니다.";

        private const string TypeHelp =
            "2행에 적을 수 있는 타입\n" +
            "\n" +
            "  int, long          정수.  1000 처럼 적습니다. 1,000 이나 12% 도 읽어줍니다\n" +
            "  float, double      실수.  1.2 (소수점은 항상 마침표. 로케일 영향 없음)\n" +
            "  bool               TRUE/FALSE, 1/0, O/X, Y/N 모두 인식\n" +
            "  string             그대로. 앞뒤 공백은 잘립니다\n" +
            "  Vector2, Vector3   1,2 / 1,2,3  (괄호를 씌워도 됩니다)\n" +
            "  Color              #RRGGBB, #RRGGBBAA, 또는 r,g,b[,a] (0~1 과 0~255 둘 다 인식)\n" +
            "  enum 이름          MonsterGrade 처럼 enum 이름을 그대로 적습니다. Enum 시트에서 만든 것도 그대로 씁니다\n" +
            "                     값은 이름(Boss) 또는 숫자(2) 로 적습니다. 대소문자는 무시합니다\n" +
            "  뒤에 []            배열이 됩니다. int[] / string[] / MonsterType[] ...\n" +
            "\n" +
            "배열 값은 대괄호로 감싸고 쉼표로 나눕니다.\n" +
            "  double[]   [0.1, 0.2]\n" +
            "  string[]   [가나다라, 음음음]\n" +
            "  원소가 하나면 괄호를 생략해도 됩니다\n" +
            "  Vector3[] 처럼 원소 자체에 쉼표가 있는 타입은 한 번 더 감쌉니다: [[1,2,3], [4,5,6]]\n" +
            "  ※ 원소 안에 쉼표가 들어가는 문자열은 배열로 표현할 수 없습니다\n" +
            "\n" +
            "빈 칸 처리\n" +
            "  · 숫자는 0, bool은 false, string은 빈 문자열, 배열은 길이 0으로 채워집니다\n" +
            "  · 0에 의미가 있는 컬럼이라면 시트에 0을 직접 적어두는 편이 안전합니다\n" +
            "\n" +
            "타입 칸을 비우거나 - 를 적으면 그 컬럼은 코드에도 데이터에도 들어가지 않습니다.\n" +
            "필드명이 비었거나 #으로 시작할 때도 마찬가지입니다. (기획용 메모 컬럼에 쓰세요)";

        private const string KeyHelp =
            "왼쪽 첫 번째 '유효한' 컬럼이 자동으로 키가 됩니다.\n" +
            "  · 유효하다 = 필드명과 타입이 모두 채워져 있고 #이 아닌 컬럼\n" +
            "  · table.Get(1) / table[1] / table.TryGet(1, out var row) 로 조회합니다\n" +
            "  · 키 타입은 int 뿐 아니라 string, enum 도 됩니다\n" +
            "  · 키가 중복되면 첫 행만 살아남고 콘솔에 에러가 찍힙니다\n" +
            "  · 전체 순회는 foreach (var row in table) 또는 table.Rows\n" +
            "\n" +
            "쉼표가 들어간 문자열은 시트에서 그냥 쓰면 됩니다. CSV 인용 처리는 임포터가 합니다.";

        private const string SyncHelp =
            "[동기화 확인]은 시트를 전부 내려받아 지금 프로젝트와 맞춰만 봅니다. 아무것도 고치지 않습니다.\n" +
            "\n" +
            "  동기화됨            스크립트도 에셋도 시트와 같음\n" +
            "  생성 안 됨          스크립트나 에셋이 아직 없음\n" +
            "  스크립트 갱신 필요   컬럼이 추가/삭제되거나 타입이 바뀜 (무엇이 바뀌었는지 함께 표시)\n" +
            "  데이터 갱신 필요     컬럼은 같지만 값이 다름 (행 수 차이와 다른 행 번호를 표시)\n" +
            "  오류                시트를 읽지 못함 (공유 설정, gid, 시작 행/열, 서식 문제)\n" +
            "\n" +
            "등록된 시트뿐 아니라 문서에 있는 탭 전체와도 대조합니다.\n" +
            "  · 문서에 있는데 위 목록에 없는 탭    → '등록되지 않은 탭'으로 표시\n" +
            "  · 위 목록에 있는데 문서에 없는 gid   → '문서에서 사라진 탭'으로 표시\n" +
            "  · gid는 같은데 탭 이름만 바뀐 경우    → '탭 이름과 테이블 이름이 다름'으로 표시\n" +
            "\n" +
            "탭 이름이 바뀌면 '전체 임포트'가 테이블 이름을 탭 쪽으로 맞춥니다. gid가 고유키이고 시트가 원본이기 때문입니다.\n" +
            "이때 예전 이름의 스크립트와 에셋은 지워지므로, 게임 코드에서 그 타입을 참조하고 있으면 함께 고쳐야 합니다.\n" +
            "자동으로 따라가는 게 곤란하면 설정의 '탭 이름 따라가기'를 끄세요.\n" +
            "[문서 탭 불러오기]를 누르면 미등록 탭이 시트 항목으로 자동 추가됩니다.\n" +
            "탭 목록은 htmlview 페이지에서 읽으므로 API 키가 필요 없습니다.\n" +
            "\n" +
            "시트를 구분하는 고유키는 gid입니다. 테이블 이름은 언제든 바꿔도 되고,\n" +
            "바꾼 뒤 임포트하면 예전 이름으로 만들어졌던 스크립트와 에셋을 자동으로 지웁니다.\n" +
            "\n" +
            "시트 항목을 통째로 지운 경우처럼 주인이 없어진 파일은 '쓰이지 않는 파일'로 표시되고,\n" +
            "[정리] 버튼이나 다음 [전체 임포트] 때 삭제됩니다.\n" +
            "gid가 같은 시트가 둘 이상이면 임포트를 시작하지 않고 알려줍니다.";

        private const string EnumHelp =
            "enum 은 데이터와 별도 문서에서 관리합니다. 문서를 나눠 두면 데이터 문서의 탭 목록이 곧 테이블 목록이 되고,\n" +
            "'데이터 탭 불러오기'에 enum 탭이 섞여 들어오지 않습니다.\n" +
            "\n" +
            "  문서 ID  : S3Settings 에셋의 '열거형 문서 ID'\n" +
            "  탭 하나  = enum 하나\n" +
            "  등록 방식: 데이터 시트와 똑같이 gid로 등록합니다. 'enum 탭 불러오기'로 한 번에 추가할 수 있습니다\n" +
            "\n" +
            "MonsterGrade 와 DamageMask 가 필요하면 탭 2개를 만들고 Enum 목록에 2개를 등록합니다.\n" +
            "\n" +
            "  [MonsterGrade 탭]                    [DamageMask 탭]\n" +
            "    name     value   comment             name    flags   comment\n" +
            "    string   int     string              string  bool    string\n" +
            "    Normal           일반                 None    TRUE    0 을 직접 적어도 됩니다\n" +
            "    Elite            정예                 Fire    TRUE    → 1\n" +
            "    Boss     10      보스                 Ice     TRUE    → 2\n" +
            "    Named            → 이어서 11          Poison  TRUE    → 4\n" +
            "\n" +
            "  name      필수. 값 이름\n" +
            "  value     선택. 비우면 자동 — 보통 직전 값 +1, flags 면 2의 거듭제곱\n" +
            "  flags     선택. 한 줄이라도 TRUE 면 [Flags] 가 붙습니다\n" +
            "  comment   선택. 생성 코드에 XML 주석으로 들어갑니다\n" +
            "\n" +
            "서식·시작 좌표·메모 무시 규칙은 데이터 시트와 똑같고, 시작 좌표는 탭마다 따로 지정합니다.\n" +
            "gid가 고유키라 탭 이름을 바꾸면 enum 이름도 따라갑니다. (설정의 '탭 이름 따라가기')\n" +
            "\n" +
            "이름은 PascalCase 로 다듬어집니다. '몬스터 등급' → 몬스터등급, 'low hp' → LowHp\n" +
            "생성 위치는 Generated/Enums/ 이고, 데이터 클래스와 같은 네임스페이스라 타입 칸에 이름만 적으면 됩니다.\n" +
            "목록에서 뺀 enum 의 파일은 '정리' 버튼이나 다음 임포트 때 지워집니다.";

        private const string ShareHelp =
            "문서 ID 는 S3Settings 에셋에 있습니다. 시트 항목은 그중 어느 문서인지(문서)와 탭(gid)만 가집니다.\n" +
            "문서를 늘리거나 바꿀 일이 생기면 그 에셋만 고치면 됩니다.\n" +
            "  Assets/Resources/S3/S3Settings.asset\n" +
            "\n" +
            "이 임포터는 API 키 없이 CSV export 주소를 그대로 읽습니다. 그래서 문서가 (전부) 열려 있어야 합니다.\n" +
            "  구글 시트 → 우측 상단 [공유] → '일반 액세스'를 '링크가 있는 모든 사용자'로,\n" +
            "  역할은 '뷰어'로 두면 됩니다.\n" +
            "비공개 상태면 CSV 대신 로그인 HTML이 돌아오고, 임포터가 그 상황을 알아채 안내합니다.\n" +
            "\n" +
            "gid는 시트 탭마다 다릅니다. 탭을 클릭했을 때 주소창 끝의 #gid=... 숫자를 그대로 적으세요.";

        private const string RuntimeHelp =
            "빌드된 게임에서 최신 데이터를 받아오는 방법\n" +
            "\n" +
            "  1) 에디터에서 한 번 임포트해 둡니다. 문서 ID와 gid가 에셋에 함께 구워지고,\n" +
            "     네트워크가 없을 때 쓸 기본 데이터가 됩니다.\n" +
            "  2) TD/S3/S3 Database 에셋을 만들고 테이블들을 등록합니다.\n" +
            "  3) 로딩 씬에서 await database.RefreshAsync(); 한 줄이면 끝입니다.\n" +
            "\n" +
            "갱신은 항상 이 순서로 물러납니다.\n" +
            "  다운로드 성공 → 최신 데이터\n" +
            "  실패 → 기기에 저장된 지난번 데이터 (persistentDataPath/S3Cache)\n" +
            "  그것도 없음 → 빌드에 구워진 데이터\n" +
            "따라서 네트워크가 없어도 게임은 반드시 뜹니다.\n" +
            "\n" +
            "주의: 에디터 플레이 모드에서 RefreshAsync를 부르면 에셋의 내용이 실제로 바뀝니다.\n" +
            "구워둔 값과 시트가 다르면 에셋이 더티 상태가 되니 그대로 저장하지 않도록 주의하세요.";

        private const float MinLogHeight = 60f;
        private const float MinBodyHeight = 100f;
        private const float SplitterHeight = 5f;
        private const float LogHeaderHeight = 22f;

        private S3ImportSettings _settings;
        private System.Collections.Generic.List<string> _orphans;
        private Vector2 _scroll;
        private Vector2 _logScroll;
        private float _logHeight = 140f;
        private bool _draggingSplitter;
        private int _lastLogLength = -1;
        private GUIStyle _helpStyle;
        private bool _showLayout;
        private bool _showRegion;
        private bool _showTypes;
        private bool _showKey;
        private bool _showEnum;
        private bool _showShare;
        private bool _showSync;
        private bool _showRuntime;

        private S3UploadTarget _uploadTarget = S3UploadTarget.Test;
        private System.Collections.Generic.List<S3PublishStatus> _awsReport;
        private bool _isAwsBusy;

        private bool _showPreview;
        private int _previewIndex;
        private Vector2 _previewScroll;

        private void OnEnable()
        {
            _settings = S3ImportSettings.Find();
        }

        /// <summary>이 창은 <c>TD/데이터 도구</c> 안에 얹혀서 그려지므로 public 이다.</summary>
        public void OnGUI()
        {
            if (_settings == null)
            {
                DrawMissingSettings();
                return;
            }

            // 로그 영역은 창 아래쪽에 고정으로 자리를 잡아두고, 위쪽 본문에 남은 높이를 준다.
            // 이렇게 하지 않으면 본문이 길어질 때 로그가 창 밖으로 밀려 잘린다.
            var logHeight = Mathf.Clamp(_logHeight, MinLogHeight, Mathf.Max(MinLogHeight, position.height - MinBodyHeight));
            var bodyHeight = Mathf.Max(MinBodyHeight, position.height - logHeight - LogHeaderHeight - SplitterHeight);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(bodyHeight));

            EditorGUI.BeginChangeCheck();

            DrawSettings();
            EditorGUILayout.Space();
            DrawSheets();
            EditorGUILayout.Space();
            DrawEnums();

            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(_settings);

            EditorGUILayout.Space();
            DrawActions();
            EditorGUILayout.Space();
            DrawHelp();

            EditorGUILayout.EndScrollView();

            DrawSplitter();
            DrawLog(logHeight);
        }

        /// <summary>본문과 로그 사이의 경계. 끌어서 로그 영역 높이를 바꿀 수 있다.</summary>
        private void DrawSplitter()
        {
            var rect = GUILayoutUtility.GetRect(0f, SplitterHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.25f));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeVertical);

            var e = Event.current;

            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
            {
                _draggingSplitter = true;
                e.Use();
            }
            else if (_draggingSplitter && e.type == EventType.MouseDrag)
            {
                _logHeight -= e.delta.y;
                Repaint();
                e.Use();
            }
            else if (_draggingSplitter && e.type == EventType.MouseUp)
            {
                _draggingSplitter = false;
                e.Use();
            }
        }

        private void DrawMissingSettings()
        {
            EditorGUILayout.HelpBox("임포터 설정 에셋이 없습니다.", MessageType.Info);

            if (GUILayout.Button("설정 에셋 만들기", GUILayout.Height(28f)))
            {
                _settings = S3ImportSettings.GetOrCreate();
            }

            _settings = (S3ImportSettings)EditorGUILayout.ObjectField(
                "기존 설정 지정", _settings, typeof(S3ImportSettings), false);
        }

        private void DrawSettings()
        {
            EditorGUILayout.LabelField("설정", EditorStyles.boldLabel);

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.ObjectField("설정 에셋", _settings, typeof(S3ImportSettings), false);
                DrawRuntimeSettingsRow();

                // 문서 ID 는 S3Settings 에셋에 있다. 여기서는 보여주기만 한다.
                DrawDocumentId("데이터 문서", S3Config.SpreadsheetId, "S3Settings 의 '기획 데이터 문서 ID'. 탭 하나 = 테이블 하나.");
                DrawDocumentId("로컬라이징 문서", S3Config.LocalizationSpreadsheetId,
                    "S3Settings 의 '로컬라이징 문서 ID'. 데이터 문서와 규칙은 같고 문서만 다르다.\n" +
                    "시트 항목의 '문서' 를 이쪽으로 고르면 여기서 받아온다.");
                DrawDocumentId("enum 문서", S3Config.EnumSpreadsheetId, "S3Settings 의 '열거형 문서 ID'. 탭 하나 = enum 하나.");

                _settings.generatedNamespace = EditorGUILayout.TextField("네임스페이스", _settings.generatedNamespace);
                _settings.scriptOutputFolder = EditorGUILayout.TextField("스크립트 폴더", _settings.scriptOutputFolder);
                _settings.assetOutputFolder = EditorGUILayout.TextField("에셋 폴더", _settings.assetOutputFolder);

                _settings.syncTableNameWithTab = EditorGUILayout.Toggle(
                    new GUIContent("탭 이름 따라가기",
                        "임포트할 때 문서 탭 이름에 맞춰 테이블 이름을 바꾼다. 시트가 원본이 된다.\n" +
                        "예전 이름의 스크립트·에셋은 지워지므로 게임 코드에서 참조 중이면 함께 고쳐야 한다."),
                    _settings.syncTableNameWithTab);
            }
        }

        /// <summary>
        /// 배포 주소·문서 ID 를 담은 런타임 설정 에셋. 없으면 만들 수 있게 해 둔다 —
        /// 이게 없으면 게임이 데이터를 아예 못 받아오므로, 새 프로젝트에서 가장 먼저 걸리는 자리다.
        /// </summary>
        private static void DrawRuntimeSettingsRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var settings = S3Settings.Instance;

                EditorGUILayout.ObjectField("배포 설정", settings, typeof(S3Settings), false);

                if (settings != null) return;

                if (GUILayout.Button("만들기", EditorStyles.miniButton, GUILayout.Width(60f)))
                {
                    Selection.activeObject = S3Settings.GetOrCreate();
                }
            }
        }

        /// <summary>설정 에셋에 적힌 문서 ID를 보여준다.</summary>
        private static void DrawDocumentId(string label, string documentId, string tooltip)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var missing = string.IsNullOrWhiteSpace(documentId);

                EditorGUILayout.LabelField(
                    new GUIContent(label, tooltip),
                    new GUIContent(missing ? "(비어 있음 — S3Settings 에셋을 채우세요)" : documentId));

                using (new EditorGUI.DisabledScope(missing))
                {
                    if (GUILayout.Button("열기", EditorStyles.miniButton, GUILayout.Width(40f)))
                    {
                        Application.OpenURL(S3Config.EditUrl(documentId, "0"));
                    }
                }
            }
        }

        /// <summary>enum 문서의 탭 목록. 데이터 시트 목록과 같은 모양이다.</summary>
        private void DrawEnums()
        {
            if (!DrawSectionFoldout("Enums", $"Enum ({_settings.enums.Count})")) return;

            var removeIndex = -1;

            for (int i = 0; i < _settings.enums.Count; i++)
            {
                var entry = _settings.enums[i];

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        entry.enabled = EditorGUILayout.ToggleLeft(GUIContent.none, entry.enabled, GUILayout.Width(18f));
                        entry.enumName = EditorGUILayout.TextField(entry.enumName);

                        var hasName = !string.IsNullOrWhiteSpace(entry.enumName);
                        EditorGUILayout.LabelField(
                            hasName ? $"→ Enums/{entry.enumName}.cs" : "→ enum 이름 필요",
                            EditorStyles.miniLabel, GUILayout.Width(180f));

                        using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(S3Config.EnumSpreadsheetId)))
                        {
                            if (GUILayout.Button("열기", EditorStyles.miniButton, GUILayout.Width(40f)))
                            {
                                Application.OpenURL(S3Config.EditUrl(S3Config.EnumSpreadsheetId, entry.gid));
                            }
                        }

                        if (GUILayout.Button("−", EditorStyles.miniButton, GUILayout.Width(24f))) removeIndex = i;
                    }

                    using (new EditorGUI.IndentLevelScope())
                    {
                        entry.gid = EditorGUILayout.TextField(
                            new GUIContent("gid (고유키)", "enum 문서 안 탭의 gid. 이 값이 enum 을 구분한다."), entry.gid);

                        DrawLayoutField(
                            new GUIContent("데이터 시작", "name 이 적힌 행 번호와 시작 열."),
                            entry.layout, l => entry.layout = l);

                        if (!string.IsNullOrEmpty(entry.generatedEnumName) && entry.generatedEnumName != entry.enumName)
                        {
                            EditorGUILayout.HelpBox(
                                $"이름이 '{entry.generatedEnumName}' 에서 바뀌었습니다. 임포트하면 예전 파일을 지우고 새로 만듭니다.",
                                MessageType.Info);
                        }
                    }
                }
            }

            if (removeIndex >= 0) _settings.enums.RemoveAt(removeIndex);

            if (GUILayout.Button("enum 추가"))
            {
                _settings.enums.Add(new S3EnumEntry());
                GUI.FocusControl(null);
            }
        }

        /// <summary>
        /// 시트를 <b>문서별 구획</b>으로 나눠 접이식으로 그린다.
        /// 목록에 없는 문서를 손으로 적어둔 항목도 구획이 생기게 해서, 어디에도 안 보이는 항목이 없게 한다.
        /// </summary>
        private void DrawSheets()
        {
            var documents = new List<string>(S3Config.TableDocumentIds);

            foreach (var entry in _settings.sheets)
            {
                if (!documents.Contains(entry.NormalizedDocumentId)) documents.Add(entry.NormalizedDocumentId);
            }

            for (int i = 0; i < documents.Count; i++)
            {
                if (i > 0) EditorGUILayout.Space();
                DrawSheetSection(documents[i]);
            }
        }

        private void DrawSheetSection(string documentId)
        {
            var entries = new List<S3SheetEntry>();
            foreach (var entry in _settings.sheets)
            {
                if (entry.NormalizedDocumentId == documentId) entries.Add(entry);
            }

            var title = $"{DocumentLabel(documentId)} 시트 ({entries.Count})";
            if (!DrawSectionFoldout(documentId, title)) return;

            S3SheetEntry removeEntry = null;

            foreach (var entry in entries)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        entry.enabled = EditorGUILayout.ToggleLeft(GUIContent.none, entry.enabled, GUILayout.Width(18f));
                        entry.tableName = EditorGUILayout.TextField(entry.tableName);

                        var hasName = !string.IsNullOrWhiteSpace(entry.tableName);
                        EditorGUILayout.LabelField(
                            hasName ? $"→ {S3CodeGenerator.TableClassName(entry.tableName)}" : "→ 테이블 이름 필요",
                            EditorStyles.miniLabel, GUILayout.Width(180f));

                        if (GUILayout.Button("열기", EditorStyles.miniButton, GUILayout.Width(40f)))
                        {
                            Application.OpenURL(S3Config.EditUrl(entry.NormalizedDocumentId, entry.gid));
                        }

                        if (GUILayout.Button("−", EditorStyles.miniButton, GUILayout.Width(24f))) removeEntry = entry;
                    }

                    using (new EditorGUI.IndentLevelScope())
                    {
                        entry.gid = EditorGUILayout.TextField(
                            new GUIContent("gid (고유키)", "시트 탭 주소 끝의 #gid=... 값. 문서와 짝지어 시트를 구분한다."), entry.gid);

                        DrawDocumentPicker(entry);

                        DrawLayout(entry);

                        if (!string.IsNullOrEmpty(entry.generatedTableName) && entry.generatedTableName != entry.tableName)
                        {
                            EditorGUILayout.HelpBox(
                                $"이름이 '{entry.generatedTableName}' 에서 바뀌었습니다. 임포트하면 예전 산출물을 지우고 새로 만듭니다.",
                                MessageType.Info);
                        }
                    }
                }
            }

            if (removeEntry != null) _settings.sheets.Remove(removeEntry);

            if (GUILayout.Button("시트 추가"))
            {
                // 이 구획의 문서로 만든다. 기본 문서면 비워 둬서 기존 항목과 같은 모양이 되게.
                _settings.sheets.Add(new S3SheetEntry
                {
                    documentId = documentId == S3Config.SpreadsheetId ? string.Empty : documentId,
                });

                GUI.FocusControl(null);
            }
        }

        /// <summary>구획 하나의 접기/펴기. 창을 다시 열어도 상태가 남게 EditorPrefs 에 둔다.</summary>
        private static bool DrawSectionFoldout(string key, string title)
        {
            var prefsKey = "KwonStudio.S3.Importer.Section." + key;
            var expanded = EditorPrefs.GetBool(prefsKey, false);

            var next = EditorGUILayout.Foldout(expanded, title, true, EditorStyles.foldoutHeader);
            if (next != expanded) EditorPrefs.SetBool(prefsKey, next);

            return next;
        }

        /// <summary>
        /// 이 시트가 어느 문서에 있는지 고른다. 기본 데이터 문서면 <c>documentId</c> 를 비워 둔다 —
        /// 문서가 하나였던 시절에 만들어진 항목과 같은 모양이 되게.
        /// </summary>
        private static void DrawDocumentPicker(S3SheetEntry entry)
        {
            var documents = new List<string>(S3Config.TableDocumentIds);
            if (documents.Count <= 1) return;

            // 목록에 없는 문서를 손으로 적어둔 경우까지 그대로 보여준다. 조용히 다른 문서로 바꾸지 않는다.
            var current = documents.IndexOf(entry.NormalizedDocumentId);
            if (current < 0)
            {
                documents.Add(entry.NormalizedDocumentId);
                current = documents.Count - 1;
            }

            var labels = new string[documents.Count];
            for (int i = 0; i < documents.Count; i++) labels[i] = DocumentLabel(documents[i]);

            var picked = EditorGUILayout.Popup(
                new GUIContent("문서", "이 탭이 있는 구글 문서. 목록은 S3Settings 의 문서 ID 들에서 온다."),
                current, labels);

            if (picked == current) return;

            entry.documentId = documents[picked] == S3Config.SpreadsheetId ? string.Empty : documents[picked];
        }

        private static string DocumentLabel(string documentId)
        {
            if (documentId == S3Config.SpreadsheetId) return "데이터";
            if (documentId == S3Config.LocalizationSpreadsheetId) return "로컬라이징";

            return documentId;
        }

        /// <summary>데이터 블록이 시작되는 행/열을 입력받는다.</summary>
        private static void DrawLayout(S3SheetEntry entry)
        {
            DrawLayoutField(
                new GUIContent("데이터 시작", "필드명이 적힌 행 번호와, 데이터가 시작되는 열. 그 위/왼쪽은 읽지 않는다."),
                entry.layout,
                layout => entry.layout = layout);
        }

        private static void DrawLayoutField(GUIContent label, S3Layout layout, System.Action<S3Layout> apply)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);

                var row = EditorGUILayout.IntField(layout.HeaderRowNumber, GUILayout.Width(38f));
                GUILayout.Label("행", GUILayout.Width(16f));

                var column = EditorGUILayout.TextField(layout.StartColumnText, GUILayout.Width(38f));
                GUILayout.Label("열", GUILayout.Width(16f));

                if (row != layout.HeaderRowNumber || column != layout.StartColumnText)
                {
                    layout = new S3Layout(Mathf.Max(1, row), column);
                    apply(layout);
                }

                GUILayout.Label(
                    layout.IsStartColumnValid
                        ? $"→ 필드명 {layout}, 타입 {layout.StartColumnName}{layout.HeaderRowNumber + 1}, 데이터 {layout.StartColumnName}{layout.HeaderRowNumber + 2} 부터"
                        : "→ 열 표기가 잘못됐습니다 (A, B, AA 또는 1, 2)",
                    EditorStyles.miniLabel);
            }
        }

        private void DrawActions()
        {
            using (new EditorGUI.DisabledScope(S3Importer.IsRunning || EditorApplication.isCompiling))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("전체 임포트", GUILayout.Height(30f)))
                    {
                        AssetDatabase.SaveAssets();
                        S3Importer.Run(_settings, false);
                    }

                    if (GUILayout.Button(
                            new GUIContent("동기화 확인", "시트를 전부 읽어 지금 스크립트·에셋과 맞춰만 봅니다. 아무것도 고치지 않습니다."),
                            GUILayout.Height(30f), GUILayout.Width(110f)))
                    {
                        AssetDatabase.SaveAssets();
                        S3Importer.CheckSync(_settings);
                    }

                    if (GUILayout.Button("코드만 생성", GUILayout.Height(30f), GUILayout.Width(100f)))
                    {
                        AssetDatabase.SaveAssets();
                        S3Importer.Run(_settings, true);
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(
                            new GUIContent("데이터 탭 불러오기", "데이터 문서에 있지만 시트 목록에 없는 탭을 찾아 추가합니다.")))
                    {
                        S3Importer.ImportMissingTabs(_settings, false);
                    }

                    using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(S3Config.EnumSpreadsheetId)))
                    {
                        if (GUILayout.Button(
                                new GUIContent("enum 탭 불러오기", "enum 문서에 있지만 Enum 목록에 없는 탭을 찾아 추가합니다.")))
                        {
                            S3Importer.ImportMissingTabs(_settings, true);
                        }
                    }
                }

                DrawPreview();
                DrawAws();
                DrawCleanup();
            }

            if (S3Importer.IsRunning || EditorApplication.isCompiling)
            {
                EditorGUILayout.HelpBox(
                    EditorApplication.isCompiling ? "컴파일 중입니다..." : "임포트 중입니다...", MessageType.Info);
            }
        }

        /// <summary>어떤 시트에도 속하지 않게 된 파일이 있으면 알려주고 지울 수 있게 한다.</summary>
        /// <summary>
        /// 임포트해둔 시트 원본을 표로 본다.
        /// 테이블이 에셋이 아니라 인스펙터로 값을 볼 수 없으므로, 그 자리를 여기서 메운다.
        /// </summary>
        private void DrawPreview()
        {
            var entries = _settings != null ? _settings.sheets : null;
            if (entries == null || entries.Count == 0) return;

            EditorGUILayout.Space();
            _showPreview = EditorGUILayout.Foldout(_showPreview, "테이블 내용 보기", true);
            if (!_showPreview) return;

            var names = new string[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                names[i] = string.IsNullOrWhiteSpace(entries[i].tableName)
                    ? $"(이름 없음 {i})"
                    : entries[i].tableName;
            }

            _previewIndex = Mathf.Clamp(_previewIndex, 0, entries.Count - 1);
            _previewIndex = EditorGUILayout.Popup("시트", _previewIndex, names);

            S3TablePreview.Draw(_settings, entries[_previewIndex], ref _previewScroll);
        }

        /// <summary>
        /// 시트에서 굽는 것과 별개로, 시트 내용을 배포용 버킷에 올리는 자리.
        /// 굽기는 이 프로젝트의 코드·에셋을 갱신하고, 업로드는 배포된 게임이 받아갈 데이터를 갱신한다.
        /// </summary>
        private void DrawAws()
        {
            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("배포", EditorStyles.boldLabel, GUILayout.Width(30f));

                _uploadTarget = (S3UploadTarget)EditorGUILayout.EnumPopup(_uploadTarget, GUILayout.Width(70f));

                using (new EditorGUI.DisabledScope(_isAwsBusy))
                {
                    if (GUILayout.Button(new GUIContent(
                            "AWS로 업로드",
                            "활성화된 시트를 다시 받아 배포 버킷에 올립니다. 예전 키는 지우지 않습니다.")))
                    {
                        PublishAsync().Forget();
                    }

                    if (GUILayout.Button(new GUIContent(
                            "AWS 상태 보기",
                            "버킷에 올라간 객체 목록을 받아 시트 내용과 대조합니다."), GUILayout.Width(110f)))
                    {
                        InspectAsync().Forget();
                    }

                    if (GUILayout.Button(new GUIContent("설정", "AWS 키·버킷·리전"), GUILayout.Width(46f)))
                    {
                        AwsSettingsWindow.Open();
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("점검", EditorStyles.boldLabel, GUILayout.Width(30f));

                if (GUILayout.Button(new GUIContent(
                        "오프라인 번들 다시 만들기",
                        "마지막 임포트 때 받아둔 캐시로 오프라인 모드가 읽을 번들을 다시 만듭니다.")))
                {
                    S3OfflineBundle.RebuildAndLog();
                }

                if (GUILayout.Button(new GUIContent(
                        "서버 시각 확인",
                        "배포 서버가 알려주는 시각과 기기 시계를 대조합니다."), GUILayout.Width(110f)))
                {
                    S3ServerTimeCheck.Check();
                }
            }

            if (!AwsCredentials.IsConfigured)
            {
                EditorGUILayout.HelpBox("AWS 설정이 비어 있습니다. [설정]에서 키와 버킷을 먼저 넣으세요.", MessageType.Info);
                return;
            }

            DrawAwsReport();
        }

        private void DrawAwsReport()
        {
            if (_awsReport == null || _awsReport.Count == 0) return;

            var orphans = new System.Collections.Generic.List<string>();
            foreach (var status in _awsReport)
            {
                if (status.IsOrphan) orphans.Add(status.Key);
            }

            if (orphans.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    $"시트에 없는 키가 {orphans.Count}개 있습니다. 탭을 지웠거나 이름을 바꾼 흔적입니다.\n" +
                    "아직 이 키를 읽는 배포본이 남아 있으면 그 빌드는 실행되지 않으니, 보통 1~2 릴리즈 뒤에 지웁니다.",
                    MessageType.Warning);

                using (new EditorGUI.DisabledScope(_isAwsBusy))
                {
                    if (GUILayout.Button($"고아 키 {orphans.Count}개 삭제"))
                    {
                        DeleteOrphansAsync(orphans).Forget();
                    }
                }
            }

            foreach (var status in _awsReport)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(status.Summary, EditorStyles.miniLabel, GUILayout.Width(96f));
                    GUILayout.Label(status.TableName, EditorStyles.miniLabel, GUILayout.Width(180f));

                    GUILayout.Label(
                        status.ExistsRemote
                            ? $"{status.Size:N0} bytes   {status.LastModifiedUtc.ToLocalTime():yyyy-MM-dd HH:mm}"
                            : "-",
                        EditorStyles.miniLabel, GUILayout.Width(190f));

                    if (!string.IsNullOrEmpty(status.Note))
                    {
                        GUILayout.Label(status.Note, EditorStyles.miniLabel);
                    }
                }
            }
        }

        private async Cysharp.Threading.Tasks.UniTaskVoid PublishAsync()
        {
            _isAwsBusy = true;
            try
            {
                await S3CdnPublisher.PublishAsync(_settings, _uploadTarget);
                _awsReport = await S3CdnPublisher.InspectAsync(_settings, _uploadTarget);
            }
            finally
            {
                _isAwsBusy = false;
                Repaint();
            }
        }

        private async Cysharp.Threading.Tasks.UniTaskVoid DeleteOrphansAsync(
            System.Collections.Generic.List<string> keys)
        {
            _isAwsBusy = true;
            try
            {
                var deleted = await S3CdnPublisher.DeleteOrphansAsync(_uploadTarget, keys);

                // 지웠으면 목록이 달라졌으니 다시 읽는다. 취소했으면 그대로 둔다.
                if (deleted > 0) _awsReport = await S3CdnPublisher.InspectAsync(_settings, _uploadTarget);
            }
            finally
            {
                _isAwsBusy = false;
                Repaint();
            }
        }

        private async Cysharp.Threading.Tasks.UniTaskVoid InspectAsync()
        {
            _isAwsBusy = true;
            try
            {
                _awsReport = await S3CdnPublisher.InspectAsync(_settings, _uploadTarget);
            }
            finally
            {
                _isAwsBusy = false;
                Repaint();
            }
        }

        private void DrawCleanup()
        {
            // 매 프레임 폴더를 훑지 않도록 리페인트 주기에 맞춰 갱신한다.
            if (_orphans == null) _orphans = S3Cleanup.FindOrphans(_settings);
            if (_orphans.Count == 0) return;

            EditorGUILayout.HelpBox(
                $"쓰이지 않는 파일 {_orphans.Count}개가 있습니다.\n" + string.Join("\n", _orphans.ToArray()),
                MessageType.Warning);

            if (GUILayout.Button($"정리 ({_orphans.Count}개 삭제)"))
            {
                var deleted = S3Cleanup.DeleteOrphans(_settings);
                AssetDatabase.SaveAssets();
                Debug.Log($"[S3] 쓰이지 않는 파일 {deleted.Count}개를 지웠습니다.\n{string.Join("\n", deleted.ToArray())}");
                _orphans = null;
            }
        }

        private void DrawHelp()
        {
            EditorGUILayout.LabelField("시트 작성 규칙", EditorStyles.boldLabel);

            DrawHelpSection("1. 시트 구조와 필드명", ref _showLayout, LayoutHelp);
            DrawHelpSection("2. 시작 행/열과 메모 섞기", ref _showRegion, RegionHelp);
            DrawHelpSection("3. 쓸 수 있는 타입", ref _showTypes, TypeHelp);
            DrawHelpSection("4. 키 컬럼과 조회", ref _showKey, KeyHelp);
            DrawHelpSection("5. Enum 시트", ref _showEnum, EnumHelp);
            DrawHelpSection("6. 문서 설정과 gid", ref _showShare, ShareHelp);
            DrawHelpSection("7. 동기화 확인 · 이름 동기화 · 정리", ref _showSync, SyncHelp);
            DrawHelpSection("8. 런타임 갱신", ref _showRuntime, RuntimeHelp);
        }

        private void DrawHelpSection(string title, ref bool expanded, string body)
        {
            expanded = EditorGUILayout.Foldout(expanded, title, true);
            if (!expanded) return;

            // 표 모양을 유지해야 해서 줄바꿈 없는 고정폭 라벨로 그린다.
            // 실제 크기를 재서 넘겨야 창이 좁을 때 본문 스크롤뷰에 가로 스크롤이 생긴다.
            if (_helpStyle == null)
            {
                _helpStyle = new GUIStyle(EditorStyles.label)
                {
                    font = EditorStyles.miniFont,
                    wordWrap = false,
                    richText = false,
                };
            }

            using (new EditorGUI.IndentLevelScope())
            {
                var size = _helpStyle.CalcSize(new GUIContent(body));
                EditorGUILayout.SelectableLabel(body, _helpStyle, GUILayout.Width(size.x), GUILayout.Height(size.y));
            }
        }

        private void DrawLog(float viewHeight)
        {
            var log = S3Importer.LogText;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("로그", EditorStyles.boldLabel);
                if (GUILayout.Button("지우기", EditorStyles.miniButton, GUILayout.Width(50f)))
                {
                    S3Importer.ClearLog();
                    _lastLogLength = 0;
                }
            }

            var text = string.IsNullOrEmpty(log) ? "아직 실행한 적이 없습니다." : log;
            var style = EditorStyles.wordWrappedMiniLabel;

            // 내용 높이를 직접 재서 라벨에 준다. ExpandHeight로는 보이는 만큼만 잡혀 스크롤이 되지 않는다.
            var contentWidth = Mathf.Max(50f, position.width - 24f);
            var contentHeight = style.CalcHeight(new GUIContent(text), contentWidth);

            // 새 줄이 붙으면 자동으로 맨 아래를 따라간다. (임포트 중 진행 상황이 계속 쌓인다)
            if (log.Length != _lastLogLength)
            {
                _lastLogLength = log.Length;
                _logScroll.y = float.MaxValue;
            }

            _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.Height(viewHeight));
            EditorGUILayout.SelectableLabel(text, style, GUILayout.Width(contentWidth), GUILayout.Height(contentHeight));
            EditorGUILayout.EndScrollView();
        }

        public void OnInspectorUpdate()
        {
            // 다운로드/컴파일 진행 상황이 로그에 반영되도록 주기적으로 다시 그린다.
            // 이 주기에만 고아 파일 목록을 다시 훑는다. (OnGUI에서 매번 훑으면 낭비다)
            _orphans = null;
            Repaint();
        }
    }
}
