using System;
using System.Text;

namespace Sportive.API.Utils;

public static class ZatcaQrGenerator
{
    /// <summary>
    /// Generates ZATCA Phase 1 compatible TLV Base64 QR Code.
    /// </summary>
    public static string GenerateQrCode(
        string sellerName, 
        string taxNumber, 
        DateTime timestamp, 
        decimal invoiceTotal, 
        decimal vatTotal)
    {
        var tlvs = new byte[][]
        {
            GetTlv(1, sellerName),
            GetTlv(2, taxNumber),
            GetTlv(3, timestamp.ToString("yyyy-MM-ddTHH:mm:ssZ")),
            GetTlv(4, invoiceTotal.ToString("0.00")),
            GetTlv(5, vatTotal.ToString("0.00"))
        };

        var totalLen = 0;
        foreach (var tlv in tlvs) totalLen += tlv.Length;

        var buffer = new byte[totalLen];
        var offset = 0;

        foreach (var tlv in tlvs)
        {
            Buffer.BlockCopy(tlv, 0, buffer, offset, tlv.Length);
            offset += tlv.Length;
        }

        return Convert.ToBase64String(buffer);
    }

    private static byte[] GetTlv(int tag, string value)
    {
        var valBytes = Encoding.UTF8.GetBytes(value);
        var tlv = new byte[2 + valBytes.Length];
        
        tlv[0] = (byte)tag;
        tlv[1] = (byte)valBytes.Length;
        Buffer.BlockCopy(valBytes, 0, tlv, 2, valBytes.Length);
        
        return tlv;
    }
}
