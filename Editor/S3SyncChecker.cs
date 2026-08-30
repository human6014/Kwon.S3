using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace KwonStudio.S3.EditorTools
{
    /// <summary>시트 한 장과 프로젝트가 얼마나 어긋나 있는지.</summary>
    public enum S3SyncStatus
    {
        /// <summary>스크립트도 에셋도 시트와 같다.</summary>
        InSync,

        /// <summary>아직 한 번도 생성하지 않았다.</summary>
        NotGenerated,

        /// <summary>컬럼이 바뀌어 스크립트를 다시 만들어야 한다.</summary>
        ScriptOutdated,

        /// <summary>스크립트는 맞지만 에셋의 값이 시트와 다르다.</summary>
        AssetOutdated,

        /// <summary>시트를 읽지 못했다.</summary>
        Error,
    }

    /// <summary>시트 한 장에 대한 확인 결과.</summary>
    public sealed class S3SyncEntryResult
    {
        public string TableName;
        public string Gid;
        public S3SyncStatus Status;
        public readonly List<string> Details = new List<string>();

        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case S3SyncStatus.InSync: return "동기화됨";
                    case S3SyncStatus.NotGenerated: return "생성 안 됨";
                    case S3SyncStatus.ScriptOutdated: return "스크립트 갱신 필요";
                    case S3SyncStatus.AssetOutdated: return "데이터 갱신 필요";
                    default: return "오류";
                }
            }
        }
    }

    /// <summary>전체 확인 결과.</summary>
    public sealed class S3SyncReport
    {
        public readonly List<S3SyncEntryResult> Entries = new List<S3SyncEntryResult>();
        public readonly List<string> Orphans = new List<string>();

        /// <summary>문서에는 있는데 설정에 등록되지 않은 탭.</summary>
        public readonly List<S3TabInfo> UnregisteredTabs = new List<S3TabInfo>();

        /// <summary>설정에는 있는데 문서에서 사라진 gid.</summary>
        public readonly List<string> MissingTabs = new List<string>();

        /// <summary>gid는 같은데 탭 이름과 테이블 이름이 어긋난 경우.</summary>
        public readonly List<string> RenamedTabs = new List<string>();

        /// <summary>탭 목록을 못 읽었을 때의 사유. 성공했으면 null.</summary>
        public string TabListError;

        public bool IsFullySynced
        {
            get
            {
                if (Orphans.Count > 0) return false;
                if (UnregisteredTabs.Count > 0 || MissingTabs.Count > 0 || RenamedTabs.Count > 0) return false;

                foreach (var entry in Entries)
                {
                    if (entry.Status != S3SyncStatus.InSync) return false;
                }

                return true;
            }
        }
    }

    /// <summary>
    /// 시트를 전부 내려받아 지금 프로젝트에 있는 스크립트·에셋과 맞춰 본다.
    /// 아무것도 고치지 않고 결과만 알려주는 읽기 전용 동작이다.
    /// </summary>
    public static class S3SyncChecker
    {
        /// <summary>문서의 탭 목록과 설정을 맞춰 본다. 미등록·삭제된 탭과 이름 불일치를 찾아낸다.</summary>
        public static void CompareTabs(
            S3ImportSettings settings, string documentId, List<S3TabInfo> tabs, S3SyncReport report)
        {
            // 이 문서에 속한 항목만 본다. 다른 문서의 시트를 여기서 '사라진 탭'으로 볼 수 없다.
            var inDocument = settings.sheets.Where(e => e.NormalizedDocumentId == documentId).ToList();

            var registered = new HashSet<string>();
            foreach (var entry in inDocument) registered.Add(entry.NormalizedGid);

            var byGid = new Dictionary<string, S3TabInfo>();

            foreach (var tab in tabs)
            {
                byGid[tab.Gid] = tab;
                if (!registered.Contains(tab.Gid)) report.UnregisteredTabs.Add(tab);
            }

            foreach (var entry in inDocument)
            {
                if (!byGid.TryGetValue(entry.NormalizedGid, out var tab))
                {
                    report.MissingTabs.Add($"{entry.tableName} (gid {entry.NormalizedGid})");
                    continue;
                }

                // gid는 같은데 탭 이름만 바뀐 경우. 임포트하면 테이블 이름이 탭을 따라간다.
                var desired = S3TabParser.ToTableName(tab.Name);
                if (desired == entry.tableName) continue;

                report.RenamedTabs.Add($"gid {entry.NormalizedGid}: 탭 '{tab.Name}' → 테이블 이름 '{entry.tableName}' (임포트하면 '{desired}' 로 바뀜)");
            }
        }

        /// <summary>이미 받아둔 CSV로 시트 한 장을 확인한다.</summary>
        public static S3SyncEntryResult CheckSheet(S3ImportSettings settings, S3SheetEntry entry, string csv)
        {
            var result = new S3SyncEntryResult { TableName = entry.tableName, Gid = entry.NormalizedGid };

            S3Sheet sheet;
            try
            {
                sheet = S3Schema.Parse(S3Csv.Parse(csv), entry.tableName, entry.layout);
            }
            catch (S3Exception e)
            {
                result.Status = S3SyncStatus.Error;
                result.Details.Add(e.Message);
                return result;
            }

            // 이름이 바뀐 흔적이 있으면 예전 산출물이 남아 있다는 뜻이다.
            if (!string.IsNullOrEmpty(entry.generatedTableName) && entry.generatedTableName != entry.tableName)
            {
                result.Details.Add($"이름이 '{entry.generatedTableName}' → '{entry.tableName}' 로 바뀌었습니다. 예전 산출물이 남아 있습니다.");
            }

            var tableType = S3TableBuilder.FindType(S3TableBuilder.TableTypeName(settings, entry.tableName));
            if (tableType == null)
            {
                result.Status = S3SyncStatus.NotGenerated;
                result.Details.Add($"컬럼 {sheet.Columns.Count}개 / 데이터 {sheet.DataRowCount}행 — 아직 스크립트가 없습니다.");
                return result;
            }

            if (CompareColumns(sheet, tableType, result.Details) || !S3CodeGenerator.IsUpToDate(settings, sheet))
            {
                result.Status = S3SyncStatus.ScriptOutdated;
                if (result.Details.Count == 0) result.Details.Add("생성 코드가 지금 시트와 다릅니다.");
                return result;
            }

            // 값이 실제로 읽히는지 확인한다. 잘못된 enum 이름이나 숫자 형식이 여기서 걸린다.
            // 배포본과의 대조는 여기서 하지 않는다 — 그건 임포터 창의 'AWS 상태 보기' 몫이다.
            try
            {
                var rowCount = S3TableBuilder.Validate(settings, entry, csv).RowCount;

                result.Status = result.Details.Count > 0 ? S3SyncStatus.ScriptOutdated : S3SyncStatus.InSync;
                if (result.Status == S3SyncStatus.InSync)
                {
                    result.Details.Add($"컬럼 {sheet.Columns.Count}개 / 데이터 {rowCount}행");
                }
            }
            catch (Exception e)
            {
                result.Status = S3SyncStatus.ScriptOutdated;
                result.Details.Add("시트 값을 읽지 못했습니다: " + e.Message);
            }

            return result;
        }

        /// <summary>시트 컬럼과 생성된 행 클래스의 필드를 맞춰 본다. 차이가 있으면 true.</summary>
        private static bool CompareColumns(S3Sheet sheet, Type tableType, List<string> details)
        {
            var temp = Activator.CreateInstance(tableType) as S3TableBase;
            if (temp == null) return false;

            var rowType = temp.RowType;

            var fields = new Dictionary<string, FieldInfo>();
            foreach (var field in rowType.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                fields[field.Name] = field;
            }

            var changed = false;

            foreach (var column in sheet.Columns)
            {
                if (!fields.TryGetValue(column.FieldName, out var field))
                {
                    details.Add($"컬럼 추가됨: {column.HeaderName} ({column.CSharpType})");
                    changed = true;
                    continue;
                }

                var actual = FriendlyTypeName(field.FieldType);
                if (actual != column.CSharpType)
                {
                    details.Add($"타입 바뀜: {column.HeaderName} — 코드 {actual} / 시트 {column.CSharpType}");
                    changed = true;
                }

                fields.Remove(column.FieldName);
            }

            foreach (var leftover in fields.Keys)
            {
                details.Add($"컬럼 없어짐: {leftover} (코드에는 있지만 시트에 없음)");
                changed = true;
            }

            return changed;
        }

        /// <summary>필드 타입을 시트 타입 표기와 같은 모양으로 만든다.</summary>
        private static string FriendlyTypeName(Type type)
        {
            if (type.IsArray) return FriendlyTypeName(type.GetElementType()) + "[]";

            if (type == typeof(int)) return "int";
            if (type == typeof(long)) return "long";
            if (type == typeof(float)) return "float";
            if (type == typeof(double)) return "double";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(string)) return "string";

            return type.Name;
        }
    }
}
