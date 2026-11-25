using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>T11b: category/rating for HS pay rules come only from the uploaded invoice line.</summary>
public static class HsInvoiceFacts
{
    public static string RiderCategory(JsonObject line)
    {
        string direct=line.S("riderCategory");
        if(direct!="")return direct;
        if(line["invoiceFacts"] is JsonObject facts&&facts.S("category")!="")return facts.S("category");
        if(line["breakdown"] is JsonArray rows)
            foreach(var row in rows.OfType<JsonObject>())
                if(row.S("segment")!="")return row.S("segment");
        return line.S("segment");
    }
    public static string RiderRating(JsonObject line)
    {
        string direct=line.S("riderRating");
        if(direct!="")return direct;
        if(line["invoiceFacts"] is JsonObject facts&&facts.S("rating")!="")return facts.S("rating");
        if(line["breakdown"] is JsonArray rows)
            foreach(var row in rows.OfType<JsonObject>())
                if(row.S("riderRating")!="")return row.S("riderRating");
        return "";
    }
}
