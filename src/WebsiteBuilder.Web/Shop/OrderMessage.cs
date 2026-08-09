using System.Text;
using WebsiteBuilder.Core.Entities;

namespace WebsiteBuilder.Web.Shop;

/// <summary>A cart line resolved against the catalog: the product as it is priced right now.</summary>
public sealed record PricedLine(Product Product, int Quantity)
{
    public long? TotalMinor => Product.PriceMinor * Quantity;
}

/// <summary>
/// Turns a cart into the WhatsApp message a customer sends the owner.
/// <para>
/// This <b>is</b> the checkout. There is no payment step: these sales already happen over WhatsApp,
/// and a cart that ends in a message the owner can reply to beats a card form nobody in the market
/// will complete. Paystack can slot in later behind the same button.
/// </para>
/// </summary>
public static class OrderMessage
{
    /// <summary>
    /// Sums the order, but only when every priced line shares a currency. A business selling in
    /// both cedis and dollars gets no total rather than a wrong one, and the owner confirms.
    /// </summary>
    public static (long? TotalMinor, string? Currency) Total(IReadOnlyList<PricedLine> lines)
    {
        var priced = lines.Where(l => l.Product.PriceMinor is not null).ToList();

        if (priced.Count == 0)
        {
            return (null, null);
        }

        var currencies = priced.Select(l => l.Product.Currency).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        return currencies.Count == 1
            ? (priced.Sum(l => l.TotalMinor!.Value), currencies[0])
            : (null, null);
    }

    public static string FormatMoney(long minor, string currency) => $"{currency} {minor / 100m:N2}";

    /// <summary>The plain-text order. Kept short: it is read on a phone, in a chat.</summary>
    public static string Compose(string businessName, IReadOnlyList<PricedLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        // "\n" rather than AppendLine: the message is built on Linux in production and Windows in
        // development, and an order that differs by the host's line ending is a difference nobody
        // asked for. WhatsApp treats a bare newline as a line break.
        const string newLine = "\n";

        var text = new StringBuilder();
        text.Append("Hello ").Append(businessName).Append(", I'd like to order:").Append(newLine);

        foreach (var line in lines)
        {
            text.Append("- ").Append(line.Quantity).Append(" x ").Append(line.Product.Name);

            if (line.TotalMinor is { } lineTotal)
            {
                text.Append(" (").Append(FormatMoney(lineTotal, line.Product.Currency)).Append(')');
            }

            text.Append(newLine);
        }

        var (total, currency) = Total(lines);

        if (total is not null && currency is not null)
        {
            text.Append(newLine).Append("Total: ").Append(FormatMoney(total.Value, currency));
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>The wa.me link that opens WhatsApp with the order already typed.</summary>
    public static string Link(string whatsAppNumber, string message) =>
        $"https://wa.me/{whatsAppNumber.TrimStart('+')}?text={Uri.EscapeDataString(message)}";
}
