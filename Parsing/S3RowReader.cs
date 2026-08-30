using System;
using UnityEngine;

namespace KwonStudio.S3
{
    /// <summary>
    /// 생성된 데이터 클래스의 <c>ReadFrom</c>이 쓰는 셀 접근자.
    /// 컬럼을 이름으로 찾고 타입별 메서드로 값을 꺼낸다.
    /// 리플렉션을 쓰지 않으므로 IL2CPP 빌드에서 코드 스트리핑에 걸리지 않는다.
    /// </summary>
    public sealed class S3RowReader
    {
        private readonly S3TextTable _table;
        private int _rowIndex;

        public S3RowReader(S3TextTable table)
        {
            _table = table;
        }

        /// <summary>읽을 행을 옮긴다. <see cref="S3Table{TKey,TRow}.LoadFrom"/>가 행마다 호출한다.</summary>
        public void MoveTo(int rowIndex) => _rowIndex = rowIndex;

        public string GetString(string column) => Read(column, S3Parse.String);

        public int GetInt(string column) => Read(column, S3Parse.Int);

        public long GetLong(string column) => Read(column, S3Parse.Long);

        public float GetFloat(string column) => Read(column, S3Parse.Float);

        public double GetDouble(string column) => Read(column, S3Parse.Double);

        public bool GetBool(string column) => Read(column, S3Parse.Bool);

        public Vector2 GetVector2(string column) => Read(column, S3Parse.Vector2);

        public Vector3 GetVector3(string column) => Read(column, S3Parse.Vector3);

        public Color GetColor(string column) => Read(column, S3Parse.Color);

        public T GetEnum<T>(string column) where T : struct, Enum => Read(column, S3Parse.Enum<T>);

        public string[] GetStringArray(string column) => ReadArray(column, S3Parse.String);

        public int[] GetIntArray(string column) => ReadArray(column, S3Parse.Int);

        public long[] GetLongArray(string column) => ReadArray(column, S3Parse.Long);

        public float[] GetFloatArray(string column) => ReadArray(column, S3Parse.Float);

        public double[] GetDoubleArray(string column) => ReadArray(column, S3Parse.Double);

        public bool[] GetBoolArray(string column) => ReadArray(column, S3Parse.Bool);

        public Vector2[] GetVector2Array(string column) => ReadArray(column, S3Parse.Vector2);

        public Vector3[] GetVector3Array(string column) => ReadArray(column, S3Parse.Vector3);

        public Color[] GetColorArray(string column) => ReadArray(column, S3Parse.Color);

        public T[] GetEnumArray<T>(string column) where T : struct, Enum => ReadArray(column, S3Parse.Enum<T>);

        private T Read<T>(string column, Func<string, T> parse)
        {
            var raw = GetRawCell(column);

            try
            {
                return parse(raw);
            }
            catch (S3Exception e)
            {
                throw Fail(column, e.Message);
            }
        }

        private T[] ReadArray<T>(string column, Func<string, T> parseElement)
        {
            var raw = GetRawCell(column);

            try
            {
                return S3Parse.Array(raw, parseElement);
            }
            catch (S3Exception e)
            {
                throw Fail(column, e.Message);
            }
        }

        private string GetRawCell(string column)
        {
            if (!_table.TryGetColumnIndex(column, out var index))
            {
                throw new S3Exception(
                    $"[{_table.Name}] 시트에 '{column}' 컬럼이 없습니다. 시트에서 컬럼을 지웠거나 이름을 바꿨다면 스크립트를 다시 생성해야 합니다.");
            }

            return _table.GetCell(_rowIndex, index);
        }

        private S3Exception Fail(string column, string message) =>
            new S3Exception($"[{_table.Name}] {_table.ToSheetRowNumber(_rowIndex)}행 '{column}' 컬럼: {message}");
    }
}
