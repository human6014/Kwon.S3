using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KwonStudio.S3.EditorTools
{
    /// <summary>
    /// 임포트할 때 남겨둔 CSV 원본을 표로 보여준다.
    /// <para>
    /// 테이블이 에셋이 아니게 되면서 인스펙터로 값을 보던 길이 없어졌다. 그 자리를 메우는 패널이다.
    /// <b>파싱 전 원본</b>을 보여주므로 "시트에 뭐라고 적혀 있나"를 그대로 확인할 수 있다.
    /// 값이 코드로 잘 변환되는지는 '동기화 확인' 이 따로 봐준다.
    /// </para>
    /// </summary>
    public static class S3TablePreview
    {
        private const int MaxRows = 30;
        private const float RowHeight = 18f;
        private const float MinColumnWidth = 60f;
        private const float MaxColumnWidth = 220f;

        /// <summary>캐시에서 읽어 화면에 그린다.</summary>
        /// <param name="scroll">호출한 창이 들고 있는 스크롤 위치.</param>
        public static void Draw(S3ImportSettings settings, S3SheetEntry entry, ref Vector2 scroll)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.tableName))
            {
                EditorGUILayout.HelpBox("볼 시트를 고르세요.", MessageType.Info);
                return;
            }

            var csv = S3Paths.ReadCache(entry.tableName);
            if (csv == null)
            {
                EditorGUILayout.HelpBox(
                    $"'{entry.tableName}' 의 원본이 없습니다. 한 번도 임포트하지 않았거나 Library 가 지워진 상태입니다.\n" +
                    "'전체 임포트' 를 돌리면 다시 생깁니다.",
                    MessageType.Info);
                return;
            }

            List<string[]> rows;
            try
            {
                rows = S3Csv.Parse(csv);
            }
            catch (S3Exception e)
            {
                EditorGUILayout.HelpBox("CSV 를 읽지 못했습니다: " + e.Message, MessageType.Error);
                return;
            }

            DrawGrid(rows, entry.layout, ref scroll);

            EditorGUILayout.LabelField(
                $"{S3Paths.CacheFilePath(entry.tableName)} — 마지막 임포트 시점의 원본입니다.",
                EditorStyles.miniLabel);
        }

        private static void DrawGrid(List<string[]> rows, S3Layout layout, ref Vector2 scroll)
        {
            var startRow = layout.HeaderRowIndex;
            var startColumn = layout.StartColumnIndex;

            if (startRow >= rows.Count)
            {
                EditorGUILayout.HelpBox(
                    $"시작 행({layout.HeaderRowNumber})이 파일 끝을 넘습니다. 전체 {rows.Count}행.", MessageType.Warning);
                return;
            }

            var header = rows[startRow];
            var widths = MeasureColumns(rows, startRow, startColumn, header.Length);

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(RowHeight * 12f));

            var shown = 0;

            for (int r = startRow; r < rows.Count && shown < MaxRows; r++, shown++)
            {
                var row = rows[r];

                using (new EditorGUILayout.HorizontalScope())
                {
                    // 시작 행이 필드명, 그 다음이 타입 행이다. 둘을 굵게 해서 데이터와 구분한다.
                    var style = r <= startRow + S3Format.HeaderToTypeOffset
                        ? EditorStyles.boldLabel
                        : EditorStyles.label;

                    GUILayout.Label((r + 1).ToString(), EditorStyles.miniLabel, GUILayout.Width(34f));

                    for (int c = startColumn; c < widths.Length; c++)
                    {
                        var cell = c < row.Length ? row[c] : string.Empty;
                        GUILayout.Label(cell, style, GUILayout.Width(widths[c]));
                    }
                }
            }

            EditorGUILayout.EndScrollView();

            var dataRows = rows.Count - layout.DataStartRowIndex;
            if (dataRows > MaxRows)
            {
                EditorGUILayout.LabelField(
                    $"데이터 {dataRows}행 중 앞부분만 보여줍니다.", EditorStyles.miniLabel);
            }
        }

        /// <summary>칸 길이에 맞춰 열 너비를 잡는다. 전부 같은 너비로 두면 긴 문자열이 잘려서 못 읽는다.</summary>
        private static float[] MeasureColumns(List<string[]> rows, int startRow, int startColumn, int columnCount)
        {
            foreach (var row in rows)
            {
                if (row.Length > columnCount) columnCount = row.Length;
            }

            var widths = new float[columnCount];

            for (int c = startColumn; c < columnCount; c++)
            {
                var longest = 0;
                var limit = Mathf.Min(rows.Count, startRow + MaxRows);

                for (int r = startRow; r < limit; r++)
                {
                    var row = rows[r];
                    if (c < row.Length && row[c] != null) longest = Mathf.Max(longest, row[c].Length);
                }

                widths[c] = Mathf.Clamp(longest * 8f + 12f, MinColumnWidth, MaxColumnWidth);
            }

            return widths;
        }
    }
}
