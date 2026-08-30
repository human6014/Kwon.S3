using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace KwonStudio.S3.EditorTools
{
    /// <summary>
    /// 임포트 전체 흐름을 맡는다.
    /// <list type="number">
    /// <item>시트를 CSV로 받아 Library/S3Cache 에 저장한다.</item>
    /// <item>스키마를 읽어 스크립트를 생성한다.</item>
    /// <item>스크립트가 바뀌었으면 리프레시하고, 컴파일이 끝난 뒤 캐시로 에셋 빌드를 이어서 한다.</item>
    /// </list>
    /// 도메인 리로드로 정적 상태가 날아가므로 진행 상황과 로그는 <see cref="SessionState"/>에 둔다.
    /// </summary>
    public static class S3Importer
    {
        private const string PendingKey = "KwonStudio.S3.Pending";
        private const string RunningKey = "KwonStudio.S3.Running";
        private const string LogKey = "KwonStudio.S3.Log";

        /// <summary>임포트가 진행 중인지. 창의 버튼을 잠그는 데 쓴다.</summary>
        public static bool IsRunning => SessionState.GetBool(RunningKey, false);

        /// <summary>창에 그려줄 누적 로그.</summary>
        public static string LogText => SessionState.GetString(LogKey, string.Empty);

        public static void ClearLog() => SessionState.SetString(LogKey, string.Empty);

        /// <summary>시트를 받아 스크립트와 에셋을 갱신한다.</summary>
        public static void Run(S3ImportSettings settings, bool codeOnly)
        {
            if (IsRunning)
            {
                Debug.LogWarning("[S3] 이미 임포트가 진행 중입니다.");
                return;
            }

            RunAsync(settings, codeOnly).Forget();
        }

        private static async UniTaskVoid RunAsync(S3ImportSettings settings, bool codeOnly)
        {
            SessionState.SetBool(RunningKey, true);
            ClearLog();

            try
            {
                var entries = settings.sheets.Where(e => e.enabled && !string.IsNullOrWhiteSpace(e.tableName)).ToList();
                var enumEntries = settings.enums.Where(e => e.enabled && !string.IsNullOrWhiteSpace(e.enumName)).ToList();

                if (entries.Count == 0 && enumEntries.Count == 0)
                {
                    Log("활성화된 시트가 없습니다. 창에서 시트나 enum 을 추가해 주세요.");
                    return;
                }

                Log($"임포트를 시작합니다. (시트 {entries.Count}개, enum {enumEntries.Count}개{(codeOnly ? ", 코드만" : string.Empty)})");

                // 탭 이름이 바뀌었으면 이름을 먼저 맞춘다. 그래야 아래에서 예전 산출물이 정리된다.
                await SyncNamesAsync(settings, entries, enumEntries);
                Validate(settings, entries, enumEntries);

                var scriptsChanged = false;
                var ready = new List<string>();

                // enum 은 데이터 클래스가 참조하므로 먼저 만든다. 어차피 같은 어셈블리라 한 번에 컴파일된다.
                if (enumEntries.Count > 0) scriptsChanged |= await GenerateEnumsAsync(settings, enumEntries);

                foreach (var entry in entries)
                {
                    // gid가 고유키다. 같은 시트의 테이블 이름이 바뀌었으면 예전 이름의 산출물부터 치운다.
                    if (!string.IsNullOrEmpty(entry.generatedTableName) && entry.generatedTableName != entry.tableName)
                    {
                        var removed = S3Cleanup.DeleteArtifacts(settings, entry.generatedTableName);
                        Log(removed.Count > 0
                            ? $"[{entry.tableName}] 이름이 '{entry.generatedTableName}' 에서 바뀌어 예전 산출물 {removed.Count}개를 지웠습니다."
                            : $"[{entry.tableName}] 이름이 '{entry.generatedTableName}' 에서 바뀌었습니다. (지울 산출물 없음)");

                        scriptsChanged |= removed.Count > 0;
                    }

                    var url = S3Config.CsvUrl(entry.NormalizedDocumentId, entry.gid);
                    Log($"[{entry.tableName}] 다운로드 중... (gid: {entry.gid}, 시작 {entry.layout})");

                    var csvText = await S3Downloader.GetTextAsync(url);
                    S3Paths.WriteCache(entry.tableName, csvText);

                    var sheet = S3Schema.Parse(S3Csv.Parse(csvText), entry.tableName, entry.layout);
                    Log($"[{entry.tableName}] 컬럼 {sheet.Columns.Count}개 / 데이터 {sheet.DataRowCount}행 (키: {sheet.KeyColumn.HeaderName})");

                    if (S3CodeGenerator.Generate(settings, sheet))
                    {
                        scriptsChanged = true;
                        Log($"[{entry.tableName}] 스크립트를 갱신했습니다.");
                    }

                    ready.Add(entry.NormalizedGid);
                }

                // 테이블 클래스들과 같은 컴파일에 들어가야 하므로 여기서 함께 만든다.
                // 이 목록이 곧 게임이 요구하는 테이블이 되고, 배포 번들에서 찾을 키가 된다.
                if (S3CodeGenerator.GenerateRegistry(settings, entries.Select(e => e.tableName).ToList()))
                {
                    scriptsChanged = true;
                    Log($"테이블 목록을 갱신했습니다. ({entries.Count}개)");
                }

                if (codeOnly)
                {
                    if (scriptsChanged) AssetDatabase.Refresh();
                    Log(scriptsChanged
                        ? "스크립트 생성 완료. 컴파일이 끝나면 다시 '전체 임포트'를 눌러 에셋을 채우세요."
                        : "스크립트에 바뀐 내용이 없습니다.");
                    return;
                }

                if (scriptsChanged)
                {
                    // 새 타입은 컴파일이 끝나야 존재한다. 남은 일은 리로드 후 OnScriptsReloaded에서 이어서 한다.
                    SessionState.SetString(PendingKey, string.Join(";", ready));
                    Log("스크립트가 바뀌었습니다. 컴파일이 끝나면 에셋 빌드를 이어서 진행합니다...");
                    AssetDatabase.Refresh();
                    return;
                }

                BuildFromCache(settings, ready);
            }
            catch (S3Exception e)
            {
                Log("실패: " + e.Message);
            }
            catch (Exception e)
            {
                Log("예기치 못한 오류: " + e.Message);
                Debug.LogException(e);
            }
            finally
            {
                SessionState.SetBool(RunningKey, false);
            }
        }

        /// <summary>
        /// 도메인 리로드는 진행 중이던 async 작업을 통째로 없앤다. <c>finally</c> 가 돌지 못해 진행 플래그가 남으면
        /// 창의 버튼이 그 세션 내내 잠긴 채로 있는다. 리로드를 넘어 살아남는 작업은 없으므로 여기서 푼다.
        /// (리로드 이후로 넘긴 일은 <see cref="PendingKey"/> 로만 이어진다.)
        /// </summary>
        [InitializeOnLoadMethod]
        private static void ClearRunningOnDomainReload() => SessionState.SetBool(RunningKey, false);

        /// <summary>컴파일이 끝난 뒤 남은 에셋 빌드를 이어서 처리한다.</summary>
        [DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            var pending = SessionState.GetString(PendingKey, string.Empty);
            if (string.IsNullOrEmpty(pending)) return;

            SessionState.EraseString(PendingKey);

            // 리로드 직후에는 AssetDatabase가 아직 정리 중일 수 있어 한 프레임 미룬다.
            EditorApplication.delayCall += () =>
            {
                var settings = S3ImportSettings.Find();
                if (settings == null)
                {
                    Log("설정 에셋을 찾지 못해 에셋 빌드를 건너뜁니다.");
                    return;
                }

                Log("컴파일 완료. 에셋 빌드를 이어서 진행합니다.");
                BuildFromCache(settings, pending.Split(';').Where(n => n.Length > 0).ToList());
            };
        }

        private static void BuildFromCache(S3ImportSettings settings, List<string> gids)
        {
            var built = 0;

            try
            {
                foreach (var gid in gids)
                {
                    // 이어서 하는 작업이라 그 사이 이름이 바뀌었을 수도 있다. 고유키인 gid로 다시 찾는다.
                    var entry = settings.sheets.FirstOrDefault(e => e.NormalizedGid == gid);
                    if (entry == null)
                    {
                        Log($"(gid {gid}) 설정에서 시트를 찾지 못해 건너뜁니다.");
                        continue;
                    }

                    var csvText = S3Paths.ReadCache(entry.tableName);
                    if (csvText == null)
                    {
                        Log($"[{entry.tableName}] 캐시가 없습니다. 다시 임포트해 주세요.");
                        continue;
                    }

                    var result = S3TableBuilder.Validate(settings, entry, csvText);
                    built++;

                    // 다음 임포트에서 이름 변경을 알아채려면 실제로 만든 이름을 남겨둬야 한다.
                    if (entry.generatedTableName != entry.tableName)
                    {
                        entry.generatedTableName = entry.tableName;
                        EditorUtility.SetDirty(settings);
                    }

                    Log($"[{entry.tableName}] 확인 완료 ({result.RowCount}행)");
                }

                var orphans = S3Cleanup.DeleteOrphans(settings);
                foreach (var path in orphans) Log($"쓰이지 않는 파일을 지웠습니다: {path}");

                // 방금 캐시가 갱신됐으니 오프라인 번들도 같이 새로 만든다. 그래야 저절로 최신이 유지된다.
                var offline = S3OfflineBundle.Rebuild(settings);
                if (offline > 0) Log($"오프라인 번들도 갱신했습니다. (테이블 {offline}개)");

                AssetDatabase.SaveAssets();
                Log($"완료. 테이블 {built}개를 확인했습니다.{(orphans.Count > 0 ? $" (정리 {orphans.Count}개)" : string.Empty)}");
            }
            catch (S3Exception e)
            {
                Log("실패: " + e.Message);
            }
            catch (Exception e)
            {
                Log("예기치 못한 오류: " + e.Message);
                Debug.LogException(e);
            }
        }

        /// <summary>시트를 전부 내려받아 지금 프로젝트와 어긋난 곳이 있는지만 확인한다. 아무것도 고치지 않는다.</summary>
        public static void CheckSync(S3ImportSettings settings)
        {
            if (IsRunning)
            {
                Debug.LogWarning("[S3] 이미 작업이 진행 중입니다.");
                return;
            }

            CheckSyncAsync(settings).Forget();
        }

        private static async UniTaskVoid CheckSyncAsync(S3ImportSettings settings)
        {
            SessionState.SetBool(RunningKey, true);
            ClearLog();

            try
            {
                var entries = settings.sheets.Where(e => !string.IsNullOrWhiteSpace(e.tableName)).ToList();
                var enumEntries = settings.enums.Where(e => !string.IsNullOrWhiteSpace(e.enumName)).ToList();
                Validate(settings, entries, enumEntries);
                Log($"동기화 확인을 시작합니다. (시트 {entries.Count}개, enum {enumEntries.Count}개)");

                var report = new S3SyncReport();

                // 문서에 있는 탭 전체와 대조한다. 설정에 없는 탭은 이 단계에서만 드러난다.
                // 문서마다 따로 대조해야 한다 — gid는 문서 안에서만 유일해서 섞으면 서로를 남의 탭으로 본다.
                foreach (var documentId in S3Config.TableDocumentIds)
                {
                    if (string.IsNullOrWhiteSpace(documentId)) continue;

                    try
                    {
                        var tabs = await S3SheetIndex.FetchAsync(documentId);
                        Log($"문서({documentId}) 탭 {tabs.Count}개: {string.Join(", ", tabs.Select(t => t.ToString()).ToArray())}");
                        S3SyncChecker.CompareTabs(settings, documentId, tabs, report);
                    }
                    catch (S3Exception e)
                    {
                        report.TabListError = e.Message;
                    }
                }

                foreach (var entry in entries)
                {
                    string csv;
                    try
                    {
                        csv = await S3Downloader.GetTextAsync(S3Config.CsvUrl(entry.NormalizedDocumentId, entry.gid));
                    }
                    catch (S3Exception e)
                    {
                        var failed = new S3SyncEntryResult
                        {
                            TableName = entry.tableName,
                            Gid = entry.NormalizedGid,
                            Status = S3SyncStatus.Error,
                        };
                        failed.Details.Add(e.Message);
                        report.Entries.Add(failed);
                        continue;
                    }

                    report.Entries.Add(S3SyncChecker.CheckSheet(settings, entry, csv));
                }

                if (enumEntries.Count > 0) report.Entries.Insert(0, await CheckEnumsAsync(settings, enumEntries));

                report.Orphans.AddRange(S3Cleanup.FindOrphans(settings));

                WriteReport(report);
            }
            catch (S3Exception e)
            {
                Log("실패: " + e.Message);
            }
            catch (Exception e)
            {
                Log("예기치 못한 오류: " + e.Message);
                Debug.LogException(e);
            }
            finally
            {
                SessionState.SetBool(RunningKey, false);
            }
        }

        private static void WriteReport(S3SyncReport report)
        {
            Log("──────── 결과 ────────");

            foreach (var entry in report.Entries)
            {
                Log($"[{entry.TableName}] (gid {entry.Gid}) {entry.StatusText}");
                foreach (var detail in entry.Details) Log("    · " + detail);
            }

            if (report.UnregisteredTabs.Count > 0)
            {
                Log($"등록되지 않은 탭 {report.UnregisteredTabs.Count}개 — '문서 탭 불러오기'로 추가할 수 있습니다.");
                foreach (var tab in report.UnregisteredTabs) Log("    · " + tab);
            }

            if (report.MissingTabs.Count > 0)
            {
                Log($"문서에서 사라진 탭 {report.MissingTabs.Count}개 — gid가 맞는지 확인하세요.");
                foreach (var name in report.MissingTabs) Log("    · " + name);
            }

            if (report.RenamedTabs.Count > 0)
            {
                Log($"탭 이름과 테이블 이름이 다른 곳 {report.RenamedTabs.Count}개 — '전체 임포트'가 탭 쪽으로 맞춥니다.");
                foreach (var name in report.RenamedTabs) Log("    · " + name);
            }

            if (report.TabListError != null)
            {
                Log("탭 목록을 읽지 못해 미등록 탭은 확인하지 못했습니다: " + report.TabListError);
            }

            if (report.Orphans.Count > 0)
            {
                Log($"쓰이지 않는 파일 {report.Orphans.Count}개 — '정리' 버튼으로 지울 수 있습니다.");
                foreach (var path in report.Orphans) Log("    · " + path);
            }

            Log(report.IsFullySynced
                ? "모두 동기화되어 있습니다."
                : "어긋난 곳이 있습니다. '전체 임포트'를 누르면 맞춰집니다.");
        }

        /// <summary>열거형 문서가 지금 생성된 enum 스크립트와 같은지 확인한다.</summary>
        private static async UniTask<S3SyncEntryResult> CheckEnumsAsync(S3ImportSettings settings, List<S3EnumEntry> enumEntries)
        {
            var result = new S3SyncEntryResult { TableName = "enum", Gid = $"{enumEntries.Count}개" };

            try
            {
                var definitions = await FetchEnumsAsync(settings, enumEntries, false);

                if (S3EnumGenerator.IsUpToDate(settings, definitions))
                {
                    var total = 0;
                    foreach (var definition in definitions) total += definition.Members.Count;

                    result.Status = S3SyncStatus.InSync;
                    result.Details.Add($"enum {definitions.Count}개 / 값 {total}개");
                }
                else
                {
                    result.Status = S3SyncStatus.ScriptOutdated;

                    foreach (var definition in definitions)
                    {
                        var path = S3EnumGenerator.FilePath(settings, definition.Name);
                        if (!System.IO.File.Exists(path)) result.Details.Add($"없음: {definition.Name}");
                    }

                    foreach (var path in S3EnumGenerator.FindOrphans(settings))
                    {
                        result.Details.Add($"등록되지 않은 enum 파일: {path}");
                    }

                    if (result.Details.Count == 0) result.Details.Add("값이 시트와 다릅니다.");
                }
            }
            catch (S3Exception e)
            {
                result.Status = S3SyncStatus.Error;
                result.Details.Add(e.Message);
            }

            return result;
        }

        /// <summary>
        /// 문서의 탭 이름과 테이블 이름을 맞춘다.
        /// 시트를 구분하는 건 gid이므로, 같은 gid의 탭 이름이 바뀌었으면 테이블 이름이 그걸 따라간다.
        /// (반대 방향은 문서 쓰기 권한이 필요해 불가능하다. 시트가 원본이다.)
        /// </summary>
        private static async UniTask SyncNamesAsync(
            S3ImportSettings settings, List<S3SheetEntry> entries, List<S3EnumEntry> enumEntries)
        {
            if (!settings.syncTableNameWithTab) return;

            var renamed = 0;

            foreach (var documentId in S3Config.TableDocumentIds)
            {
                var inDocument = entries.Where(e => e.NormalizedDocumentId == documentId).ToList();
                if (inDocument.Count == 0) continue;

                var tabs = await TryFetchTabsAsync(documentId, "데이터");
                if (tabs != null)
                {
                    foreach (var entry in inDocument)
                    {
                        if (!tabs.TryGetValue(entry.NormalizedGid, out var tab)) continue;

                        var desired = S3TabParser.ToTableName(tab.Name);
                        if (desired == entry.tableName) continue;

                        Log($"탭 이름이 바뀌었습니다. 테이블 이름을 맞춥니다: '{entry.tableName}' → '{desired}' (gid {entry.NormalizedGid})");
                        entry.tableName = desired;
                        renamed++;
                    }
                }
            }

            if (enumEntries.Count > 0)
            {
                var tabs = await TryFetchTabsAsync(S3Config.EnumSpreadsheetId, "enum");
                if (tabs != null)
                {
                    foreach (var entry in enumEntries)
                    {
                        if (!tabs.TryGetValue(entry.NormalizedGid, out var tab)) continue;

                        var desired = S3TabParser.ToTableName(tab.Name);
                        if (desired == entry.enumName) continue;

                        Log($"탭 이름이 바뀌었습니다. enum 이름을 맞춥니다: '{entry.enumName}' → '{desired}' (gid {entry.NormalizedGid})");
                        entry.enumName = desired;
                        renamed++;
                    }
                }
            }

            if (renamed > 0)
            {
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }
        }

        /// <summary>탭 목록을 gid로 찾을 수 있게 받아온다. 실패하면 null을 돌려주고 진행을 막지 않는다.</summary>
        private static async UniTask<Dictionary<string, S3TabInfo>> TryFetchTabsAsync(string documentId, string label)
        {
            try
            {
                var tabs = await S3SheetIndex.FetchAsync(documentId);
                var byGid = new Dictionary<string, S3TabInfo>();
                foreach (var tab in tabs) byGid[tab.Gid] = tab;

                return byGid;
            }
            catch (S3Exception e)
            {
                Log($"{label} 문서의 탭 이름을 확인하지 못해 이름 동기화를 건너뜁니다: {e.Message}");
                return null;
            }
        }

        /// <summary>등록된 enum 탭을 읽어 스크립트를 만든다. 바뀐 게 있으면 true.</summary>
        private static async UniTask<bool> GenerateEnumsAsync(S3ImportSettings settings, List<S3EnumEntry> entries)
        {
            var changed = false;

            // 이름이 바뀌었으면 예전 이름의 파일부터 치운다.
            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.generatedEnumName) || entry.generatedEnumName == entry.enumName) continue;

                if (S3EnumGenerator.DeleteFile(settings, entry.generatedEnumName))
                {
                    Log($"[enum] 이름이 '{entry.generatedEnumName}' 에서 바뀌어 예전 파일을 지웠습니다.");
                    changed = true;
                }
            }

            var definitions = await FetchEnumsAsync(settings, entries, true);

            var deleted = new List<string>();
            changed |= S3EnumGenerator.Generate(settings, definitions, deleted);

            foreach (var path in deleted) Log($"[enum] 쓰이지 않는 enum 삭제: {path}");

            foreach (var entry in entries)
            {
                if (entry.generatedEnumName == entry.enumName) continue;

                entry.generatedEnumName = entry.enumName;
                EditorUtility.SetDirty(settings);
            }

            if (changed) Log("[enum] 스크립트를 갱신했습니다.");

            return changed;
        }

        /// <summary>등록된 enum 탭을 내려받아 정의로 바꾼다.</summary>
        private static async UniTask<List<S3EnumDefinition>> FetchEnumsAsync(
            S3ImportSettings settings, List<S3EnumEntry> entries, bool verbose)
        {
            var definitions = new List<S3EnumDefinition>();

            foreach (var entry in entries)
            {
                var csv = await S3Downloader.GetTextAsync(S3Config.CsvUrl(S3Config.EnumSpreadsheetId, entry.gid));
                var definition = S3EnumSheet.Parse(csv, entry.enumName, entry.NormalizedGid, entry.layout);
                definitions.Add(definition);

                if (verbose)
                {
                    Log($"[enum] {definition.Name} (gid {entry.NormalizedGid}): 값 {definition.Members.Count}개" +
                        $"{(definition.IsFlags ? " (flags)" : string.Empty)}");
                }
            }

            return definitions;
        }

        /// <summary>문서의 탭 목록을 읽어, 설정에 없는 탭을 항목으로 추가한다.</summary>
        /// <param name="forEnums">true면 enum 문서를, false면 데이터 문서를 읽는다.</param>
        public static void ImportMissingTabs(S3ImportSettings settings, bool forEnums)
        {
            if (IsRunning)
            {
                Debug.LogWarning("[S3] 이미 작업이 진행 중입니다.");
                return;
            }

            ImportMissingTabsAsync(settings, forEnums).Forget();
        }

        private static async UniTaskVoid ImportMissingTabsAsync(S3ImportSettings settings, bool forEnums)
        {
            SessionState.SetBool(RunningKey, true);
            ClearLog();

            try
            {
                var label = forEnums ? "enum" : "데이터";

                // 데이터 쪽은 문서가 여럿일 수 있다. enum 은 지금도 문서 하나다.
                var documentIds = forEnums
                    ? new[] { S3Config.EnumSpreadsheetId }
                    : S3Config.TableDocumentIds;

                var registered = forEnums
                    ? new HashSet<string>(settings.enums.Select(e => e.NormalizedGid))
                    : new HashSet<string>(settings.sheets.Select(e => e.DocumentGidKey));

                var added = 0;

                foreach (var documentId in documentIds)
                {
                    if (string.IsNullOrWhiteSpace(documentId)) continue;

                    Log($"{label} 문서({documentId})의 탭 목록을 읽는 중...");
                    var tabs = await S3SheetIndex.FetchAsync(documentId);
                    Log($"탭 {tabs.Count}개를 찾았습니다.");

                    foreach (var tab in tabs)
                    {
                        // 데이터는 문서마다 gid가 따로 도므로 문서까지 묶어서 본다.
                        var key = forEnums ? tab.Gid : documentId + "#" + tab.Gid;

                        if (registered.Contains(key))
                        {
                            Log($"    이미 등록됨: {tab}");
                            continue;
                        }

                        var name = S3TabParser.ToTableName(tab.Name);

                        if (forEnums)
                        {
                            settings.enums.Add(new S3EnumEntry { gid = tab.Gid, enumName = name });
                            Log($"    추가: {tab} → Enums/{name}.cs");
                        }
                        else
                        {
                            settings.sheets.Add(new S3SheetEntry
                            {
                                gid = tab.Gid,
                                tableName = name,
                                // 기본 문서면 비워 둔다. 기존 항목과 같은 모양이 되게.
                                documentId = documentId == S3Config.SpreadsheetId ? string.Empty : documentId,
                            });
                            Log($"    추가: {tab} → {S3CodeGenerator.TableClassName(name)}");
                        }

                        registered.Add(key);
                        added++;
                    }
                }

                if (added > 0)
                {
                    EditorUtility.SetDirty(settings);
                    AssetDatabase.SaveAssets();
                }

                Log(added > 0
                    ? $"{added}개를 추가했습니다. 시작 행/열을 확인한 뒤 '전체 임포트'를 누르세요."
                    : "새로 추가할 탭이 없습니다.");
            }
            catch (S3Exception e)
            {
                Log("실패: " + e.Message);
            }
            catch (Exception e)
            {
                Log("예기치 못한 오류: " + e.Message);
                Debug.LogException(e);
            }
            finally
            {
                SessionState.SetBool(RunningKey, false);
            }
        }

        /// <summary>시트를 구분하는 건 gid이므로 겹치면 안 된다. 이름도 마찬가지다.</summary>
        private static void Validate(S3ImportSettings settings, List<S3SheetEntry> entries, List<S3EnumEntry> enumEntries)
        {
            var seen = new Dictionary<string, string>();
            var names = new HashSet<string>();

            if (enumEntries.Count > 0 && string.IsNullOrWhiteSpace(S3Config.EnumSpreadsheetId))
            {
                throw new S3Exception(
                    "enum 문서 ID가 비어 있습니다. Remote/S3Config.cs 의 EnumSpreadsheetId 상수를 채워주세요.");
            }

            // enum 은 다른 문서라 gid가 데이터 시트와 겹쳐도 된다. 이름만 서로 달라야 한다.
            var enumGids = new Dictionary<string, string>();
            foreach (var entry in enumEntries)
            {
                if (!entry.layout.IsStartColumnValid)
                {
                    throw new S3Exception($"[{entry.enumName}] 시작 열 표기가 잘못됐습니다. A, B, AA 처럼 적거나 1, 2 처럼 적으세요.");
                }

                if (enumGids.TryGetValue(entry.NormalizedGid, out var gidOwner))
                {
                    throw new S3Exception(
                        $"enum 문서의 gid {entry.NormalizedGid} 가 '{gidOwner}' 와 '{entry.enumName}' 두 곳에 지정돼 있습니다.");
                }

                enumGids.Add(entry.NormalizedGid, entry.enumName);

                if (!names.Add(entry.enumName))
                {
                    throw new S3Exception($"enum 이름 '{entry.enumName}' 이(가) 여러 탭에 있습니다. 이름은 서로 달라야 합니다.");
                }
            }

            foreach (var entry in entries)
            {
                if (!names.Add(entry.tableName))
                {
                    throw new S3Exception($"테이블 이름 '{entry.tableName}' 이(가) 여러 시트에 있습니다. 이름은 서로 달라야 합니다.");
                }

                if (!entry.layout.IsStartColumnValid)
                {
                    throw new S3Exception($"[{entry.tableName}] 시작 열 표기가 잘못됐습니다. A, B, AA 처럼 적거나 1, 2 처럼 적으세요.");
                }

                // 고유키는 (문서, gid) 다. gid는 문서 안에서만 유일해서 문서가 둘이면 gid 0 이 양쪽에 다 있다.
                if (seen.TryGetValue(entry.DocumentGidKey, out var owner))
                {
                    throw new S3Exception(
                        $"같은 문서의 gid {entry.NormalizedGid} 가 '{owner}' 와 '{entry.tableName}' 두 곳에 지정돼 있습니다. " +
                        "한 문서 안에서 gid는 시트마다 하나여야 합니다.");
                }

                seen.Add(entry.DocumentGidKey, entry.tableName);
            }
        }

        private static void Log(string message)
        {
            var sb = new StringBuilder(SessionState.GetString(LogKey, string.Empty));
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(message);

            SessionState.SetString(LogKey, sb.ToString());
            Debug.Log("[S3] " + message);
        }
    }
}
