namespace HdrPilot.Core.Games.Scanners.BattleNet;

/// <summary>Ein Produkt aus product.db: uid, Produktcode und Installationsordner (so wie gespeichert, meist mit "/").</summary>
internal sealed record BattleNetProductInstall(string Uid, string ProductCode, string InstallPath);

/// <summary>Gelesene Produkte und Fehler (deutsch, ohne Pfad). Fehler in einem Eintrag verhindern die übrigen nicht.</summary>
internal sealed record ProductDbContent(IReadOnlyList<BattleNetProductInstall> Installs, IReadOnlyList<string> Errors);

/// <summary>
/// Liest %ProgramData%\Battle.net\Agent\product.db (Protobuf). Verwendet werden nur die am Zielrechner geprüften Felder:
/// Datenbank Feld 1 (wiederholt) = Produkt; im Produkt Feld 1 = uid, Feld 2 = Produktcode, Feld 3 = Einstellungen;
/// in den Einstellungen Feld 1 = Installationsordner. Alle anderen Felder werden übersprungen.
/// </summary>
internal static class ProductDbReader
{
    private const int DatabaseProductInstallField = 1;
    private const int ProductUidField = 1;
    private const int ProductCodeField = 2;
    private const int ProductSettingsField = 3;
    private const int SettingsInstallPathField = 1;

    public static ProductDbContent Read(ReadOnlySpan<byte> data)
    {
        var installs = new List<BattleNetProductInstall>();
        var errors = new List<string>();
        var reader = new ProtoWireReader(data);
        var index = 0;
        try
        {
            while (!reader.End)
            {
                var (field, wireType) = reader.ReadTag();
                if (field != DatabaseProductInstallField)
                {
                    reader.Skip(wireType);
                    continue;
                }

                if (wireType != ProtoWireType.LengthDelimited)
                {
                    throw new InvalidDataException($"Produkteintrag hat den falschen Wire-Typ {(int)wireType}");
                }

                index++;
                var entry = reader.ReadLengthDelimited();
                try
                {
                    installs.Add(ReadInstall(entry));
                }
                catch (InvalidDataException ex)
                {
                    errors.Add($"Eintrag {index} in product.db ist defekt und wird übersprungen ({ex.Message}).");
                }
            }
        }
        catch (InvalidDataException ex)
        {
            errors.Add($"product.db ist defekt oder abgeschnitten (ab Byte {reader.Position}: {ex.Message}); Einträge danach fehlen.");
        }

        return new ProductDbContent(installs, errors);
    }

    private static BattleNetProductInstall ReadInstall(ReadOnlySpan<byte> data)
    {
        string? uid = null;
        string? productCode = null;
        string? installPath = null;
        var reader = new ProtoWireReader(data);
        while (!reader.End)
        {
            var (field, wireType) = reader.ReadTag();
            if (wireType == ProtoWireType.LengthDelimited && field == ProductUidField)
            {
                uid = reader.ReadString();
            }
            else if (wireType == ProtoWireType.LengthDelimited && field == ProductCodeField)
            {
                productCode = reader.ReadString();
            }
            else if (wireType == ProtoWireType.LengthDelimited && field == ProductSettingsField)
            {
                installPath = ReadInstallPath(reader.ReadLengthDelimited());
            }
            else
            {
                reader.Skip(wireType);
            }
        }

        if (string.IsNullOrWhiteSpace(uid))
        {
            throw new InvalidDataException("ohne uid");
        }

        if (string.IsNullOrWhiteSpace(productCode))
        {
            throw new InvalidDataException($"ohne Produktcode, uid {uid}");
        }

        return new BattleNetProductInstall(uid.Trim(), productCode.Trim(), installPath?.Trim() ?? string.Empty);
    }

    private static string? ReadInstallPath(ReadOnlySpan<byte> data)
    {
        string? installPath = null;
        var reader = new ProtoWireReader(data);
        while (!reader.End)
        {
            var (field, wireType) = reader.ReadTag();
            if (wireType == ProtoWireType.LengthDelimited && field == SettingsInstallPathField)
            {
                installPath = reader.ReadString();
            }
            else
            {
                reader.Skip(wireType);
            }
        }

        return installPath;
    }
}
