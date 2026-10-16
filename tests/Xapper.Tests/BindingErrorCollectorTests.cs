using System.Windows.Controls;
using System.Windows.Data;
using Xapper.Inspector.Diagnostics;

namespace Xapper.Tests;

/// <summary>
/// 바인딩 오류 수집기가 디버거 없이도 WPF 의 바인딩 트레이스를 받아 모으는지 확인한다.
/// 수집기는 프로세스 전역 트레이스 소스에 붙으므로, 다른 테스트가 낸 오류와 섞이지 않게 테스트마다 고유한 경로 이름을 쓴다.
/// </summary>
public sealed class BindingErrorCollectorTests
{
    #region Tests

    /// <summary>없는 속성을 가리키는 바인딩이 경로 오류로 모인다.</summary>
    [Fact]
    public void Collects_path_error_without_debugger()
    {
        using var collector = new BindingErrorCollector();
        collector.Start();

        StaThread.Run(() => BindBrokenPath("NoSuchProperty_Collects", rows: 1));

        var records = collector.Read(clear: false, out _);
        Assert.Contains(records, record => record.Message.Contains("NoSuchProperty_Collects"));
    }

    /// <summary>행마다 객체 해시만 다른 같은 오류는 한 건으로 합쳐 센다.</summary>
    [Fact]
    public void Merges_rows_that_differ_only_by_hash()
    {
        using var collector = new BindingErrorCollector();
        collector.Start();

        StaThread.Run(() => BindBrokenPath("NoSuchProperty_Merges", rows: 3));

        var matching = collector.Read(clear: false, out _)
            .Where(record => record.Message.Contains("NoSuchProperty_Merges"))
            .ToList();

        var only = Assert.Single(matching);
        Assert.Equal(3, only.Count);
        Assert.DoesNotContain("HashCode=", only.Message);
    }

    /// <summary>clear 로 읽으면 다음 읽기에는 그 뒤에 난 것만 남는다.</summary>
    [Fact]
    public void Clear_forgets_what_was_read()
    {
        using var collector = new BindingErrorCollector();
        collector.Start();

        StaThread.Run(() => BindBrokenPath("NoSuchProperty_Clear", rows: 1));

        var first = collector.Read(clear: true, out _);
        var second = collector.Read(clear: false, out _);

        Assert.Contains(first, record => record.Message.Contains("NoSuchProperty_Clear"));
        Assert.DoesNotContain(second, record => record.Message.Contains("NoSuchProperty_Clear"));
    }

    #endregion

    #region Private Methods

    /// <summary>없는 속성에 묶인 TextBlock 을 행 수만큼 만듭니다. 행마다 다른 데이터 객체를 줘서 해시가 달라지게 한다.</summary>
    private static void BindBrokenPath(string path, int rows)
    {
        for (var i = 0; i < rows; i++)
        {
            var block = new TextBlock { DataContext = new Row() };
            block.SetBinding(TextBlock.TextProperty, new Binding(path));
        }
    }

    #endregion

    #region Nested Types

    /// <summary>바인딩 경로가 가리키는 속성을 갖지 않는 데이터 객체.</summary>
    private sealed class Row
    {
        /// <summary>바인딩이 찾지 않는 속성.</summary>
        public string Name { get; set; } = "";
    }

    #endregion
}
