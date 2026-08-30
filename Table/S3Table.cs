using System;
using System.Collections.Generic;
using UnityEngine;

namespace KwonStudio.S3
{
    /// <summary>
    /// 시트 한 탭 = 테이블 에셋 하나. 행 목록과 키 조회를 제공한다.
    /// 생성된 테이블 클래스는 이 클래스를 상속만 하고 몸통은 비어 있다.
    /// </summary>
    /// <typeparam name="TKey">키 컬럼 타입.</typeparam>
    /// <typeparam name="TRow">행 데이터 타입.</typeparam>
    public abstract class S3Table<TKey, TRow> : S3TableBase where TRow : class, IS3Row<TKey>, new()
    {
        private List<TRow> _rows = new List<TRow>();

        // 첫 조회 시점에 만들고 그 뒤로 재사용한다. 갱신 때 무효화된다.
        private Dictionary<TKey, TRow> _lookup;

        /// <summary>시트에 적힌 순서 그대로의 행 목록.</summary>
        public IReadOnlyList<TRow> Rows => _rows;

        public override Type RowType => typeof(TRow);

        public override int Count => _rows.Count;

        /// <summary>키로 행을 얻는다. 없으면 null.</summary>
        public TRow this[TKey key] => Get(key);

        /// <summary>키로 행을 얻는다. 없으면 null을 돌려준다.</summary>
        public TRow Get(TKey key)
        {
            return TryGet(key, out var row) ? row : null;
        }

        /// <summary>키로 행을 찾는다.</summary>
        public bool TryGet(TKey key, out TRow row)
        {
            EnsureLookup();
            return _lookup.TryGetValue(key, out row);
        }

        /// <summary>해당 키의 행이 있는지 확인한다.</summary>
        public bool Contains(TKey key)
        {
            EnsureLookup();
            return _lookup.ContainsKey(key);
        }

        /// <summary>foreach 지원. List의 구조체 열거자를 그대로 넘겨 박싱을 피한다.</summary>
        public List<TRow>.Enumerator GetEnumerator() => _rows.GetEnumerator();

        /// <summary>
        /// 파싱된 시트로 행을 통째로 교체한다.
        /// 값 변환에 실패하면 예외를 던지되 기존 <see cref="Rows"/>는 건드리지 않는다.
        /// </summary>
        public override void LoadFrom(S3TextTable text)
        {
            if (text == null) throw new S3Exception("읽을 시트가 없습니다.");

            var parsed = new List<TRow>(text.RowCount);
            var reader = new S3RowReader(text);

            for (int i = 0; i < text.RowCount; i++)
            {
                reader.MoveTo(i);

                var row = new TRow();
                row.ReadFrom(reader);
                parsed.Add(row);
            }

            // 전부 성공한 뒤에야 갈아끼운다.
            _rows = parsed;
            _lookup = null;
        }

        private void EnsureLookup()
        {
            if (_lookup != null) return;

            _lookup = new Dictionary<TKey, TRow>(_rows.Count);
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                if (row == null) continue;

                if (_lookup.ContainsKey(row.Key))
                {
                    Debug.LogError($"[{TableName}] 키가 중복된 행이 있어 뒤쪽 행을 버립니다. (key: {row.Key})");
                    continue;
                }

                _lookup.Add(row.Key, row);
            }
        }
    }
}
