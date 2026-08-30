using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KwonStudio.S3.EditorTools
{
    /// <summary>
    /// 임포트할 시트 한 탭에 대한 설정.
    /// 시트를 구분하는 고유키는 <see cref="gid"/>다. 테이블 이름은 언제든 바뀔 수 있으므로,
    /// 이름이 바뀌면 <see cref="generatedTableName"/>을 보고 예전 산출물을 지운다.
    /// </summary>
    [Serializable]
    public class S3SheetEntry
    {
        [Tooltip("체크를 끄면 '전체 임포트'에서 제외된다.")]
        public bool enabled = true;

        [Tooltip("문서 안의 탭 번호. 시트 탭을 눌렀을 때 주소 끝의 #gid=... 값이다. 문서와 짝지어 시트를 구분하는 고유키다.")]
        public string gid = "0";

        [Tooltip("이 탭이 있는 문서 ID. 비우면 기본 데이터 문서(S3Config.SpreadsheetId)를 쓴다.")]
        public string documentId = string.Empty;

        [Tooltip("테이블 이름. 'Monster'라고 적으면 MonsterData / MonsterDataTable 이 만들어진다.")]
        public string tableName = string.Empty;

        [Tooltip("시트에서 데이터 블록이 시작되는 위치. 메모가 섞인 시트라면 여기를 옮긴다.")]
        public S3Layout layout = S3Layout.Default;

        [HideInInspector, Tooltip("마지막으로 실제 생성한 테이블 이름. 이름이 바뀐 걸 알아채는 데 쓴다.")]
        public string generatedTableName = string.Empty;

        public string NormalizedGid => string.IsNullOrWhiteSpace(gid) ? "0" : gid.Trim();

        /// <summary>이 탭이 있는 문서. 비워 두면 기본 데이터 문서다.</summary>
        public string NormalizedDocumentId =>
            string.IsNullOrWhiteSpace(documentId) ? S3Config.SpreadsheetId : documentId.Trim();

        /// <summary>
        /// 시트를 구분하는 진짜 고유키. <b>gid 하나로는 부족하다</b> —
        /// gid는 문서 안에서만 유일해서 문서가 둘이면 gid 0 이 양쪽에 다 있다.
        /// </summary>
        public string DocumentGidKey => NormalizedDocumentId + "#" + NormalizedGid;
    }

    /// <summary>
    /// enum 문서의 탭 하나에 대한 설정. 데이터 시트 항목과 같은 구조다.
    /// 탭 하나 = enum 하나이고, 구분하는 고유키는 <see cref="gid"/>다.
    /// </summary>
    [Serializable]
    public class S3EnumEntry
    {
        [Tooltip("체크를 끄면 '전체 임포트'에서 제외된다.")]
        public bool enabled = true;

        [Tooltip("enum 문서 안의 탭 번호. 이 값이 enum 을 구분하는 고유키다.")]
        public string gid = "0";

        [Tooltip("만들 enum 이름. 'MonsterGrade' 라고 적으면 Generated/Enums/MonsterGrade.cs 가 만들어진다.")]
        public string enumName = string.Empty;

        [Tooltip("탭에서 데이터 블록이 시작되는 위치.")]
        public S3Layout layout = S3Layout.Default;

        [HideInInspector, Tooltip("마지막으로 실제 생성한 enum 이름. 이름이 바뀐 걸 알아채는 데 쓴다.")]
        public string generatedEnumName = string.Empty;

        public string NormalizedGid => string.IsNullOrWhiteSpace(gid) ? "0" : gid.Trim();
    }

    /// <summary>
    /// 시트 임포터의 프로젝트 설정. 에디터 전용 어셈블리(KwonStudio.S3.Editor)에 속한다.
    /// 문서 ID는 <see cref="S3Config.TableDocumentIds"/>에 상수로 고정돼 있고,
    /// 시트 항목은 그중 어느 문서인지만 <see cref="S3SheetEntry.documentId"/>로 가리킨다.
    /// </summary>
    public class S3ImportSettings : ScriptableObject
    {
        // 패키지는 읽기 전용일 수 있으므로(git/registry 로 설치하면 PackageCache 에 들어간다)
        // 설정 에셋은 반드시 프로젝트의 Assets 아래에 만든다. 찾는 건 경로가 아니라 타입 검색이라
        // (아래 Find) 나중에 옮겨도 계속 찾아낸다 — 이 상수는 '처음 만들 때 놓을 자리'일 뿐이다.
        public const string DefaultFolder = "Assets/Settings/S3";
        public const string DefaultAssetPath = DefaultFolder + "/S3ImportSettings.asset";

        [Tooltip("생성되는 C# 스크립트가 놓일 폴더.")]
        public string scriptOutputFolder = "Assets/02.Script/Data";

        [Tooltip("생성되는 테이블 에셋이 놓일 폴더.")]
        public string assetOutputFolder = "Assets/08.Scriptable/S3";

        [Tooltip("생성되는 클래스의 네임스페이스.")]
        public string generatedNamespace = "Game.Data";

        [Tooltip("켜면 임포트할 때 문서 탭 이름에 맞춰 테이블 이름을 바꾼다. 시트가 원본이 된다. " +
                 "이름이 바뀌면 예전 이름의 스크립트와 에셋은 지워지므로, 게임 코드에서 참조 중이면 함께 고쳐야 한다.")]
        public bool syncTableNameWithTab = true;

        public List<S3SheetEntry> sheets = new List<S3SheetEntry>();

        [Tooltip("enum 문서의 탭들. 데이터 시트보다 먼저 생성되므로 타입 칸에 이름만 적으면 된다.")]
        public List<S3EnumEntry> enums = new List<S3EnumEntry>();

        /// <summary>프로젝트에 있는 설정 에셋을 찾는다. 없으면 null.</summary>
        public static S3ImportSettings Find()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(S3ImportSettings));
            if (guids.Length == 0) return null;

            return AssetDatabase.LoadAssetAtPath<S3ImportSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        /// <summary>설정 에셋을 찾고, 없으면 기본 경로에 새로 만든다.</summary>
        public static S3ImportSettings GetOrCreate()
        {
            var settings = Find();
            if (settings != null) return settings;

            settings = CreateInstance<S3ImportSettings>();
            S3Paths.EnsureAssetFolder(DefaultFolder);
            AssetDatabase.CreateAsset(settings, DefaultAssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[S3] 설정 에셋을 새로 만들었습니다: {DefaultAssetPath}");

            return settings;
        }
    }
}
