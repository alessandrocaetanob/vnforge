using System.Text.Json.Serialization;

namespace VNForge.Core.Text;

/// <summary>Converte enum de e para o nome declarado em <c>JsonStringEnumMemberName</c> e recusa número, para o arquivo não aceitar um valor que o formato não define.</summary>
internal sealed class StrictEnumConverter<TEnum>() : JsonStringEnumConverter<TEnum>(namingPolicy: null, allowIntegerValues: false)
    where TEnum : struct, Enum;
