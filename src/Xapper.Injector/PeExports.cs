using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace Xapper.Injector;

/// <summary>
/// 네이티브 DLL 파일의 export 테이블에서 함수의 RVA 를 읽는다.
///
/// 다른 프로세스 안의 함수 주소는 "그 프로세스에 올라간 모듈의 베이스 + 파일이 말하는 RVA" 다. 이 서버와 대상의
/// 비트 수가 같으면 이 서버에 같은 DLL 을 올려 GetProcAddress 로 구할 수도 있지만, 64비트 서버는 32비트 DLL 을
/// 올릴 수 없다. .NET Framework 앱은 기본 설정(AnyCPU, 32비트 우선)으로 32비트 프로세스가 되는 일이 흔하므로,
/// 파일을 직접 읽어 비트 수와 상관없이 같은 길로 구한다.
/// </summary>
public static class PeExports
{
    #region Constants

    /// <summary>IMAGE_EXPORT_DIRECTORY 안 필드의 오프셋.</summary>
    private const int NumberOfNamesOffset = 24;
    private const int AddressOfFunctionsOffset = 28;
    private const int AddressOfNamesOffset = 32;
    private const int AddressOfNameOrdinalsOffset = 36;

    #endregion

    #region Public Methods

    /// <summary>
    /// 이름으로 export 를 찾습니다.
    /// </summary>
    /// <param name="dllPath">읽을 DLL 파일 경로.</param>
    /// <param name="exportName">찾을 함수 이름 (대소문자 구분).</param>
    /// <returns>찾은 export. 없으면 null.</returns>
    /// <exception cref="BadImageFormatException">파일이 PE 이미지가 아닌 경우.</exception>
    public static PeExport? Find(string dllPath, string exportName)
    {
        using var stream = File.OpenRead(dllPath);
        using var pe = new PEReader(stream);

        var header = pe.PEHeaders.PEHeader;
        if (header is null)
            return null;

        var directory = header.ExportTableDirectory;
        if (directory.Size == 0)
            return null;

        var exportStart = directory.RelativeVirtualAddress;
        var exportEnd = exportStart + directory.Size;

        var numberOfNames = ReadUInt32(pe, exportStart + NumberOfNamesOffset);
        var functions = (int)ReadUInt32(pe, exportStart + AddressOfFunctionsOffset);
        var names = (int)ReadUInt32(pe, exportStart + AddressOfNamesOffset);
        var ordinals = (int)ReadUInt32(pe, exportStart + AddressOfNameOrdinalsOffset);

        for (var i = 0; i < numberOfNames; i++)
        {
            var nameRva = (int)ReadUInt32(pe, names + i * 4);
            if (!string.Equals(ReadAsciiZ(pe, nameRva), exportName, StringComparison.Ordinal))
                continue;

            var ordinal = ReadUInt16(pe, ordinals + i * 2);
            var functionRva = (int)ReadUInt32(pe, functions + ordinal * 4);

            // RVA 가 export 디렉터리 안을 가리키면 함수가 아니라 "DLL.함수" 꼴의 전달(forwarder) 문자열이다.
            return functionRva >= exportStart && functionRva < exportEnd
                ? new PeExport(0, ReadAsciiZ(pe, functionRva))
                : new PeExport((uint)functionRva, null);
        }

        return null;
    }

    #endregion

    #region Private Methods

    /// <summary>RVA 위치에서 4바이트 정수를 읽습니다.</summary>
    private static uint ReadUInt32(PEReader pe, int rva) => ReaderAt(pe, rva).ReadUInt32();

    /// <summary>RVA 위치에서 2바이트 정수를 읽습니다.</summary>
    private static ushort ReadUInt16(PEReader pe, int rva) => ReaderAt(pe, rva).ReadUInt16();

    /// <summary>RVA 위치에서 0 으로 끝나는 ASCII 문자열을 읽습니다.</summary>
    private static string ReadAsciiZ(PEReader pe, int rva)
    {
        var reader = ReaderAt(pe, rva);
        var builder = new StringBuilder();
        while (reader.RemainingBytes > 0)
        {
            var b = reader.ReadByte();
            if (b == 0)
                break;
            builder.Append((char)b);
        }

        return builder.ToString();
    }

    /// <summary>RVA 가 속한 섹션을 그 RVA 부터 읽는 리더를 돌려줍니다.</summary>
    /// <exception cref="BadImageFormatException">RVA 가 어느 섹션에도 속하지 않는 경우.</exception>
    private static BlobReader ReaderAt(PEReader pe, int rva)
    {
        var block = pe.GetSectionData(rva);
        if (block.Length == 0)
            throw new BadImageFormatException($"RVA 0x{rva:X} is outside every section of the image.");

        return block.GetReader();
    }

    #endregion
}

/// <summary>
/// DLL 이 내보낸 함수 하나. 함수 본체가 그 DLL 에 있으면 <see cref="Rva"/>, 다른 DLL 로 넘겨지면 <see cref="Forwarder"/>.
/// </summary>
/// <param name="Rva">함수의 RVA. 전달 export 면 0.</param>
/// <param name="Forwarder">"KERNELBASE.LoadLibraryW" 꼴의 전달 대상. 함수 본체가 이 DLL 에 있으면 null.</param>
public readonly record struct PeExport(uint Rva, string? Forwarder);
