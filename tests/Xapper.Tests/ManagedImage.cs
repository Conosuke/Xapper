using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Xapper.Tests;

/// <summary>
/// 원하는 메타데이터 버전 문자열을 가진 최소한의 관리 코드 exe 를 만든다.
/// .NET Framework 앱은 이 문자열로 CLR 세대를 알리는데, CLR 2 앱을 테스트 머신에 깔아 둘 수는 없으므로 직접 만든다.
/// </summary>
internal static class ManagedImage
{
    /// <summary>"v4.0.30319" 나 "v2.0.50727" 같은 메타데이터 버전을 가진 exe 를 씁니다.</summary>
    /// <param name="path">쓸 경로.</param>
    /// <param name="metadataVersion">메타데이터 루트에 적을 런타임 버전 문자열.</param>
    public static void Write(string path, string metadataVersion)
    {
        var metadata = new MetadataBuilder();
        var name = Path.GetFileNameWithoutExtension(path);

        metadata.AddModule(0, metadata.GetOrAddString(name + ".exe"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString(name), new Version(1, 0, 0, 0), default, default, 0, AssemblyHashAlgorithm.None);
        metadata.AddTypeDefinition(
            default, default, metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));

        var image = new ManagedPEBuilder(
            PEHeaderBuilder.CreateExecutableHeader(),
            new MetadataRootBuilder(metadata, metadataVersion),
            new BlobBuilder());

        var blob = new BlobBuilder();
        image.Serialize(blob);
        File.WriteAllBytes(path, blob.ToArray());
    }
}
