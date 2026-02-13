using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Sharc.Core.Primitives;

namespace Sharc;

/// <summary>
/// A type-safe registry that provides MethodInfo for the FilterStar JIT compiler.
/// Eliminates fragile string-based lookups and ensures AOT compatibility.
/// </summary>
internal static class JitMethodRegistry
{
    private static readonly BindingFlags StaticInternal = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    // RawByteComparer methods
    public static readonly MethodInfo CompareInt64 = GetMethod(typeof(RawByteComparer), nameof(RawByteComparer.CompareInt64), typeof(ReadOnlySpan<byte>), typeof(long), typeof(long));
    public static readonly MethodInfo CompareDouble = GetMethod(typeof(RawByteComparer), nameof(RawByteComparer.CompareDouble), typeof(ReadOnlySpan<byte>), typeof(double));
    public static readonly MethodInfo Utf8Compare = GetMethod(typeof(RawByteComparer), nameof(RawByteComparer.Utf8Compare), typeof(ReadOnlySpan<byte>), typeof(byte[]));
    public static readonly MethodInfo Utf8StartsWith = GetMethod(typeof(RawByteComparer), nameof(RawByteComparer.Utf8StartsWith), typeof(ReadOnlySpan<byte>), typeof(byte[]));
    public static readonly MethodInfo Utf8EndsWith = GetMethod(typeof(RawByteComparer), nameof(RawByteComparer.Utf8EndsWith), typeof(ReadOnlySpan<byte>), typeof(byte[]));
    public static readonly MethodInfo Utf8Contains = GetMethod(typeof(RawByteComparer), nameof(RawByteComparer.Utf8Contains), typeof(ReadOnlySpan<byte>), typeof(byte[]));
    public static readonly MethodInfo DecodeInt64 = GetMethod(typeof(RawByteComparer), nameof(RawByteComparer.DecodeInt64), typeof(ReadOnlySpan<byte>), typeof(long));
    public static readonly MethodInfo DecodeDouble = GetMethod(typeof(RawByteComparer), nameof(RawByteComparer.DecodeDouble), typeof(ReadOnlySpan<byte>));

    // SerialTypeCodec methods
    public static readonly MethodInfo GetContentSize = GetMethod(typeof(SerialTypeCodec), nameof(SerialTypeCodec.GetContentSize), typeof(long));
    public static readonly MethodInfo IsIntegral = GetMethod(typeof(SerialTypeCodec), nameof(SerialTypeCodec.IsIntegral), typeof(long));
    public static readonly MethodInfo IsReal = GetMethod(typeof(SerialTypeCodec), nameof(SerialTypeCodec.IsReal), typeof(long));
    public static readonly MethodInfo IsText = GetMethod(typeof(SerialTypeCodec), nameof(SerialTypeCodec.IsText), typeof(long));

    // FilterStarCompiler internal methods
    public static readonly MethodInfo GetSerialType = GetMethod(typeof(FilterStarCompiler), nameof(FilterStarCompiler.GetSerialType), typeof(ReadOnlySpan<long>), typeof(int));
    public static readonly MethodInfo GetOffset = GetMethod(typeof(FilterStarCompiler), nameof(FilterStarCompiler.GetOffset), typeof(ReadOnlySpan<int>), typeof(int));
    public static readonly MethodInfo GetSlice = GetMethod(typeof(FilterStarCompiler), nameof(FilterStarCompiler.GetSlice), typeof(ReadOnlySpan<byte>), typeof(int), typeof(int));
    public static readonly MethodInfo Utf8SetContains = GetMethod(typeof(FilterStarCompiler), nameof(FilterStarCompiler.Utf8SetContains), typeof(ReadOnlySpan<byte>), typeof(HashSet<string>));

    // External Type methods
    public static readonly MethodInfo HashSetInt64Contains = typeof(HashSet<long>).GetMethod(nameof(HashSet<long>.Contains), [typeof(long)])!;

    private static MethodInfo GetMethod(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] Type type, 
        string name, 
        params Type[] parameterTypes)
    {
        return type.GetMethod(name, StaticInternal, null, parameterTypes, null)
            ?? throw new InvalidOperationException($"Method {type.Name}.{name} not found with specified parameters.");
    }
}
