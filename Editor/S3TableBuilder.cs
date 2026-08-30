using System;
using System.Linq;

namespace KwonStudio.S3.EditorTools
{
    /// <summary>
    /// 생성된 테이블 클래스로 시트를 실제로 읽어보고 문제가 없는지 확인한다.
    /// <para>
    /// 에셋을 만들지 않는다 — 데이터는 배포본에서 받아 채우므로 구워둘 곳이 없다.
    /// 그래도 이 단계를 남기는 이유는 <b>시트의 값 오류를 임포트 시점에 잡기 위해서</b>다.
    /// 잘못된 enum 이름이나 숫자 형식은 여기서 걸리지 않으면 실행 중에야 드러난다.
    /// </para>
    /// </summary>
    public static class S3TableBuilder
    {
        public sealed class Result
        {
            public int RowCount;
        }

        /// <summary>생성된 테이블 클래스의 전체 이름.</summary>
        public static string TableTypeName(S3ImportSettings settings, string tableName)
        {
            var tableClass = S3CodeGenerator.TableClassName(tableName);

            return string.IsNullOrWhiteSpace(settings.generatedNamespace)
                ? tableClass
                : $"{settings.generatedNamespace}.{tableClass}";
        }

        /// <summary>시트를 읽어보고 행 수를 돌려준다. 값이 잘못됐으면 예외가 난다.</summary>
        public static Result Validate(S3ImportSettings settings, S3SheetEntry entry, string csv)
        {
            var typeName = TableTypeName(settings, entry.tableName);

            var tableType = FindType(typeName);
            if (tableType == null)
            {
                throw new S3Exception(
                    $"타입 '{typeName}' 을(를) 찾을 수 없습니다. 스크립트 생성 후 컴파일이 끝났는지, 콘솔에 컴파일 에러가 없는지 확인하세요.");
            }

            var table = (S3TableBase)Activator.CreateInstance(tableType);
            table.LoadFromCsv(csv, entry.layout);

            return new Result { RowCount = table.Count };
        }

        /// <summary>
        /// 생성된 타입을 이름으로 찾는다.
        /// 에디터에서만 쓰는 리플렉션이라 IL2CPP 스트리핑과 무관하고, 생성 코드가 아직 없어도 컴파일이 깨지지 않는다.
        /// </summary>
        public static Type FindType(string fullName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, false))
                .FirstOrDefault(type => type != null);
        }
    }
}
