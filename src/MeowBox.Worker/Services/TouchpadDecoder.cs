namespace MeowBox.Worker.Services;

internal static class TouchpadDecoder
{
    private const int SlotSize = 7;
    private const int FirstSlotOffset = 1;
    private const int MinimumTrailerBytes = 3;

    public static TouchpadDecodedReport? TryParse(byte[] report)
    {
        if (report.Length < FirstSlotOffset + SlotSize + MinimumTrailerBytes || report[0] != 0x04)
        {
            return null;
        }

        var slotCount = Math.Max(0, (report.Length - FirstSlotOffset - MinimumTrailerBytes) / SlotSize);
        if (slotCount == 0)
        {
            return null;
        }

        var contacts = new List<TouchpadDecodedContact>(slotCount);

        for (var slot = 0; slot < slotCount; slot++)
        {
            var offset = FirstSlotOffset + slot * SlotSize;
            if (offset + 6 >= report.Length)
            {
                break;
            }

            var flags = report[offset];
            contacts.Add(new TouchpadDecodedContact(
                slot,
                flags,
                (flags & 0x01) != 0,
                (flags & 0x02) != 0,
                (byte)((flags >> 2) & 0x07),
                BitConverter.ToUInt16(report, offset + 1),
                BitConverter.ToUInt16(report, offset + 3),
                BitConverter.ToUInt16(report, offset + 5)));
        }

        if (contacts.Count == 0)
        {
            return null;
        }

        var scanTimeOffset = FirstSlotOffset + contacts.Count * SlotSize;
        var scanTime = scanTimeOffset + 1 < report.Length ? BitConverter.ToUInt16(report, scanTimeOffset) : (ushort)0;
        var contactCount = scanTimeOffset + 2 < report.Length ? report[scanTimeOffset + 2] : (byte)contacts.Count(static contact => contact.Tip);
        var buttonByteIndex = scanTimeOffset + 3;
        var button1 = buttonByteIndex < report.Length && (report[buttonByteIndex] & 0x01) != 0;
        return new TouchpadDecodedReport(report[0], scanTime, contactCount, button1, contacts);
    }

    public static bool HasInteraction(TouchpadDecodedReport? report)
        => report is not null && (report.Button1 || report.ContactCount > 0 || report.Contacts.Any(static contact => contact.Tip || contact.Confidence));

    public static int GetCurrentPressure(TouchpadDecodedReport? report)
        => report?.Contacts
            .Where(static contact => contact.Tip || contact.Confidence)
            .Select(static contact => (int)contact.Pressure)
            .DefaultIfEmpty(0)
            .Max() ?? 0;
}

internal sealed record TouchpadDecodedReport(byte ReportId, ushort ScanTime, byte ContactCount, bool Button1, IReadOnlyList<TouchpadDecodedContact> Contacts);

internal readonly record struct TouchpadDecodedContact(int SlotIndex, byte Flags, bool Tip, bool Confidence, byte ContactId, ushort X, ushort Y, ushort Pressure);
