using KwonStudio.S3;
using UnityEditor;
using UnityEngine;

public static class PkgCheck
{
    // 1회차: 설정 에셋을 만들고 값을 채운다.
    public static void Seed()
    {
        var s = S3Settings.GetOrCreate();
        s.testBaseUrl = "https://example.invalid";
        s.liveBaseUrl = "https://live.invalid";
        s.remotePrefix = "config/v1";
        s.spreadsheetId = "SHEET_ID_TEST";
        s.localizationSpreadsheetId = "LOC_ID_TEST";
        EditorUtility.SetDirty(s);
        AssetDatabase.SaveAssets();
        Debug.Log($"PKGCHECK seeded at {AssetDatabase.GetAssetPath(s)}");
        EditorApplication.Exit(0);
    }

    // 2회차: 새 프로세스라 캐시가 비어 있다 → Resources.Load 경로를 실제로 탄다.
    public static void Verify()
    {
        var url = S3Config.BuildBundleUrl();
        var id = S3Config.SpreadsheetId;
        var docs = S3Config.TableDocumentIds;
        Debug.Log($"PKGCHECK url={url} id={id} docs={docs.Length}:{string.Join(",", docs)}");

        var ok = url == "https://example.invalid/config/v1/tables.json"
                 && id == "SHEET_ID_TEST"
                 && docs.Length == 2 && docs[1] == "LOC_ID_TEST";

        Debug.Log(ok ? "PKGCHECK OK" : "PKGCHECK FAIL");
        EditorApplication.Exit(ok ? 0 : 1);
    }
}
