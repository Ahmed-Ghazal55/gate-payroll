using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SubmissionTests
{
    [TestMethod] public void LegacyResponseEditsPreserveReviewAndOriginalIdentity()
    {
        var d=new JsonObject { ["id"]="test",["iqama"]="2000000888",["name"]="Original Rider",["workSystem"]="tiers",["createdAt"]="2026-10-01T00:00:00Z",["status"]="approved",["reviewNote"]="Identity checked",["reviewedBy"]="reviewer",["reviewedAt"]="2026-10-02T00:00:00Z" };
        SubmissionChoices.ArchiveAndEdit(d,new("2000000888","Changed Rider","commission"));
        Assert.AreEqual(2,SubmissionChoices.Version(d));Assert.IsTrue(d.B("isEdited"));Assert.AreEqual("pending",d.S("status"));Assert.AreEqual("2000000888",d.S("iqama"));Assert.AreEqual("2026-10-01T00:00:00Z",d.S("createdAt"));Assert.IsFalse(d.ContainsKey("reviewNote"));Assert.IsFalse(d.ContainsKey("reviewedBy"));
        var original=d["history"]![0]!.AsObject();Assert.AreEqual("approved",original.S("status"));Assert.AreEqual("Identity checked",original.S("reviewNote"));Assert.AreEqual("reviewer",original.S("reviewedBy"));Assert.AreEqual(1,SubmissionChoices.Version(original));Assert.AreEqual(d.S("createdAt"),original.S("submittedAt"));
        SubmissionChoices.ArchiveAndEdit(d,new("2000000888","Third Rider","tiers"));Assert.AreEqual(3,SubmissionChoices.Version(d));Assert.AreEqual(2,d["history"]!.AsArray().Count);Assert.AreEqual("Original Rider",original.S("name"));Assert.AreEqual("Changed Rider",d["history"]![1]!.S("name"));
    }
    [TestMethod] public void VersionGuardsRejectStaleReviewAndEdit()
    {
        var d=new JsonObject { ["version"]=2 };
        SubmissionChoices.CheckVersion(d,new JsonObject { ["version"]=2 });
        var error=Assert.ThrowsExactly<ApiError>(()=>SubmissionChoices.CheckVersion(d,new JsonObject { ["version"]=1 }));Assert.AreEqual(409,error.Status);
        SubmissionChoices.CheckVersion(new JsonObject(),new JsonObject { ["version"]=1 });
    }
    [TestMethod] public void AdministrativeProjectionNeverExposesEditCredential()
    {
        var d=new JsonObject { ["id"]="synthetic",["editTokenHash"]="secret",["history"]=new JsonArray() };
        var result=SubmissionChoices.PublicAdmin(d);Assert.IsFalse(result.ContainsKey("editTokenHash"));Assert.AreEqual("secret",d.S("editTokenHash"));Assert.IsNotNull(result["history"]);
    }
}
