using Microsoft.OpenApi;
namespace Payroll;

public static partial class ApiContracts
{
    static OpenApiSchema EvaluationStaff() => O(new() { ["id"] = S(), ["name"] = S(), ["cities"] = A(S()), ["departments"] = A(S()), ["roles"] = A(S()), ["apps"] = A(S()), ["active"] = B(), ["excluded"] = B(), ["pendingRoleApproval"] = B(), ["allCities"] = B(), ["allApps"] = B() }, "id", "name", "cities", "departments", "roles", "apps");
    static OpenApiSchema EvaluationSection() => O(new() { ["key"] = S(), ["title"] = S(), ["titleEn"] = S(), ["titleUr"] = S(), ["kind"] = S(), ["sharingToken"] = S(), ["added"] = A(S()), ["removed"] = A(S()), ["staff"] = A(EvaluationStaff()) }, "key", "title", "kind", "sharingToken");
    static OpenApiSchema EvaluationRound() => O(new() { ["id"] = S(), ["title"] = S(), ["status"] = S(), ["kind"] = S(), ["schema"] = S(), ["sharingToken"] = S(), ["staff"] = A(EvaluationStaff()), ["cities"] = A(S()), ["apps"] = A(S()), ["sections"] = A(EvaluationSection()), ["createdAt"] = S(), ["updatedAt"] = S(), ["schemaVersion"] = N(), ["allowedActions"] = A(S()) }, "id", "title", "status", "sharingToken", "staff", "cities", "apps", "allowedActions");
    static OpenApiSchema EvaluationLabel() => O(new() { ["id"] = S(), ["labelAr"] = S(), ["labelEn"] = S(), ["labelUr"] = S() }, "id", "labelAr", "labelEn", "labelUr");
    static OpenApiSchema EvaluationSchema() => O(new()
    {
        ["schema"] = S(), ["roundId"] = S(), ["title"] = S(), ["titleEn"] = S(), ["titleUr"] = S(), ["noticeAr"] = S(), ["noticeEn"] = S(), ["noticeUr"] = S(),
        ["cities"] = A(S()), ["apps"] = A(S()), ["staff"] = A(EvaluationStaff()), ["identityFields"] = A(EvaluationLabel()),
        ["staffRatingMin"] = N(), ["staffRatingMax"] = N(), ["appRatingMin"] = N(), ["appRatingMax"] = N(),
        ["staffDimensions"] = A(EvaluationLabel()), ["staffReasons"] = A(EvaluationLabel()), ["appReasons"] = A(EvaluationLabel()), ["factors"] = A(EvaluationLabel()),
        ["sectionKey"] = S(), ["sectionKind"] = S(), ["includeFinanceCard"] = B(),
        ["financeFactors"] = A(EvaluationLabel()), ["financeReasons"] = A(EvaluationLabel()),
        ["factorMode"] = S(), ["editPolicyPending"] = B(), ["uploadsPolicyPending"] = B(), ["allowedActions"] = A(S()),
        ["attachmentLimits"] = O(new() { ["imagesPerCard"] = N(), ["voicePerCard"] = N(), ["voiceSeconds"] = N(), ["replyBytes"] = N() })
    }, "schema", "roundId", "title", "staff", "cities", "apps", "staffDimensions", "staffReasons", "appReasons", "factors", "allowedActions");
    static OpenApiSchema EvaluationCard() => O(new() { ["staffId"] = S(), ["app"] = S(), ["reason"] = S(), ["ratings"] = O(EvaluationSurvey.StaffDimensions.ToDictionary(d => d, _ => (IOpenApiSchema)N())), ["overall"] = N(), ["factorRatings"] = O(EvaluationSurvey.Factors.ToDictionary(d => d, _ => (IOpenApiSchema)N())), ["notes"] = S(), ["complaint"] = B(), ["complaintDetails"] = S(), ["attachments"] = A(Free()) });
    static OpenApiSchema EvaluationFinanceAnswer() => O(new() { ["reason"] = S(), ["overall"] = N(), ["factorRatings"] = O(EvaluationSurvey.FinanceFactors.ToDictionary(d => d, _ => (IOpenApiSchema)N())), ["notes"] = S(), ["complaint"] = B(), ["complaintDetails"] = S(), ["attachments"] = A(Free()) });
    static OpenApiSchema EvaluationReply() => O(new() { ["id"] = S(), ["roundId"] = S(), ["sectionKey"] = S(), ["sectionTitle"] = S(), ["schemaVersion"] = N(), ["iqama"] = S(), ["name"] = S(), ["city"] = S(), ["nationality"] = S(), ["phone"] = S(), ["workApps"] = A(S()), ["companyId"] = S(), ["identityVerified"] = B(), ["staffAnswers"] = A(EvaluationCard()), ["appAnswers"] = A(EvaluationCard()), ["financeAnswer"] = EvaluationFinanceAnswer(), ["createdAt"] = S() }, "id", "roundId", "iqama", "name", "city", "nationality", "workApps", "staffAnswers", "appAnswers", "createdAt");
    static OpenApiSchema EvaluationBucket() => O(new()
    {
        ["key"] = S(), ["app"] = S(), ["count"] = N(), ["responseCount"] = N(), ["complaintCount"] = N(), ["ratedCount"] = N(), ["skippedCount"] = N(),
        ["average"] = new OpenApiSchema { Type = JsonSchemaType.Number | JsonSchemaType.Null },
        ["staffRatedCount"] = N(), ["appRatedCount"] = N(), ["staffAverage"] = new OpenApiSchema { Type = JsonSchemaType.Number | JsonSchemaType.Null }, ["appAverage"] = new OpenApiSchema { Type = JsonSchemaType.Number | JsonSchemaType.Null },
        ["reasons"] = A(O(new() { ["key"] = S(), ["count"] = N() }, "key", "count")),
        ["dimensions"] = A(O(new() { ["key"] = S(), ["count"] = N(), ["average"] = new OpenApiSchema { Type = JsonSchemaType.Number | JsonSchemaType.Null } }, "key", "count", "average")),
        ["distribution"] = A(O(new() { ["stars"] = N(), ["count"] = N() }, "stars", "count"))
    }, "key");
    static OpenApiSchema NullableNumber() => new() { Type = JsonSchemaType.Number | JsonSchemaType.Null };
    static OpenApiSchema EvaluationDrillDown() => O(new()
    {
        ["target"] = S(), ["city"] = S(), ["app"] = S(), ["department"] = S(), ["supervisorId"] = S(), ["nationalityId"] = S()
    }, "target");
    static OpenApiSchema EvaluationFactorStat() => O(new() { ["key"] = S(), ["count"] = N(), ["average"] = NullableNumber() }, "key", "count");
    static OpenApiSchema EvaluationSupervisorScorecardItem() => O(new()
    {
        ["staffId"] = S(), ["name"] = S(), ["department"] = S(), ["replyCount"] = N(), ["complaintCount"] = N(),
        ["complaintRate"] = NullableNumber(), ["average"] = NullableNumber(), ["rankInDepartment"] = NullableNumber(),
        ["changeVsPrevious"] = NullableNumber(), ["lowSample"] = B(), ["identityVisible"] = B(),
        ["dimensions"] = A(O(new() { ["key"] = S(), ["count"] = N(), ["average"] = NullableNumber() }, "key", "count")),
        ["bySection"] = A(O(new() { ["sectionKey"] = S(), ["sectionTitle"] = S(), ["replyCount"] = N(), ["average"] = NullableNumber() }, "sectionKey", "replyCount")),
        ["drillDown"] = EvaluationDrillDown()
    }, "staffId", "name", "replyCount", "lowSample", "identityVisible", "dimensions");
    static OpenApiSchema EvaluationFinanceScorecardItem() => O(new()
    {
        ["app"] = S(), ["ratedCount"] = N(), ["skippedCount"] = N(), ["average"] = NullableNumber(),
        ["factors"] = A(EvaluationFactorStat()), ["lowSample"] = B()
    }, "app", "ratedCount", "factors");
    static OpenApiSchema EvaluationFinanceScorecard() => O(new()
    {
        ["roundId"] = S(), ["totalResponses"] = N(), ["confidenceNoteAr"] = S(), ["labelsAr"] = StringMap(),
        ["items"] = A(EvaluationFinanceScorecardItem()), ["allowedActions"] = A(S())
    }, "roundId", "totalResponses", "confidenceNoteAr", "items", "allowedActions");
    static OpenApiSchema EvaluationSupervisorScorecard() => O(new()
    {
        ["roundId"] = S(), ["totalResponses"] = N(), ["confidenceNoteAr"] = S(), ["labelsAr"] = StringMap(),
        ["items"] = A(EvaluationSupervisorScorecardItem()), ["allowedActions"] = A(S())
    }, "roundId", "totalResponses", "confidenceNoteAr", "items", "allowedActions");
    static OpenApiSchema EvaluationAppScorecardItem() => O(new()
    {
        ["app"] = S(), ["replyCount"] = N(), ["average"] = NullableNumber(), ["netScore"] = NullableNumber(),
        ["distribution"] = A(O(new() { ["stars"] = N(), ["count"] = N() }, "stars", "count")),
        ["topFactors"] = A(EvaluationFactorStat()), ["bottomFactors"] = A(EvaluationFactorStat()),
        ["lowSample"] = B(), ["identityVisible"] = B(), ["drillDown"] = EvaluationDrillDown()
    }, "app", "replyCount", "distribution", "topFactors", "bottomFactors", "lowSample", "identityVisible");
    static OpenApiSchema EvaluationAppScorecard() => O(new()
    {
        ["roundId"] = S(), ["totalResponses"] = N(), ["confidenceNoteAr"] = S(), ["labelsAr"] = StringMap(),
        ["items"] = A(EvaluationAppScorecardItem()), ["allowedActions"] = A(S())
    }, "roundId", "totalResponses", "confidenceNoteAr", "items", "allowedActions");
    static OpenApiSchema EvaluationHeatmap() => O(new()
    {
        ["roundId"] = S(), ["totalResponses"] = N(), ["dimensions"] = A(S()), ["confidenceNoteAr"] = S(), ["labelsAr"] = StringMap(),
        ["cells"] = A(O(new()
        {
            ["staffId"] = S(), ["name"] = S(), ["dimension"] = S(), ["replyCount"] = N(), ["average"] = NullableNumber(),
            ["lowSample"] = B(), ["identityVisible"] = B(), ["drillDown"] = EvaluationDrillDown()
        }, "staffId", "dimension", "replyCount", "lowSample", "identityVisible")),
        ["allowedActions"] = A(S())
    }, "roundId", "totalResponses", "dimensions", "confidenceNoteAr", "cells", "allowedActions");
    static OpenApiSchema EvaluationCityAppMatrix() => O(new()
    {
        ["roundId"] = S(), ["totalResponses"] = N(), ["cities"] = A(S()), ["apps"] = A(S()), ["confidenceNoteAr"] = S(), ["labelsAr"] = StringMap(),
        ["cells"] = A(O(new()
        {
            ["city"] = S(), ["app"] = S(), ["replyCount"] = N(), ["average"] = NullableNumber(),
            ["lowSample"] = B(), ["identityVisible"] = B(), ["drillDown"] = EvaluationDrillDown()
        }, "city", "app", "replyCount", "lowSample", "identityVisible")),
        ["allowedActions"] = A(S())
    }, "roundId", "totalResponses", "cities", "apps", "confidenceNoteAr", "cells", "allowedActions");
    static OpenApiSchema? EvaluationResponse(string method, string path)
    {
        bool get = method == "GET";
        if (path.StartsWith("/api/forms/evaluations/", StringComparison.Ordinal))
        {
            if (path.EndsWith("/staff/import", StringComparison.Ordinal)) return O(new() { ["imported"] = N(), ["excluded"] = N() }, "imported", "excluded");
            if (System.Text.RegularExpressions.Regex.IsMatch(path, @"/staff/\{[^}]+\}$")) return EvaluationStaff();
            if (path.EndsWith("/staff", StringComparison.Ordinal)) return A(EvaluationStaff());
            if (path.EndsWith("/responses", StringComparison.Ordinal)) return Page(EvaluationReply());
            if (path.EndsWith("/statistics/{dimension}", StringComparison.Ordinal)) return O(new() { ["roundId"] = S(), ["totalResponses"] = N(), ["items"] = A(EvaluationBucket()) }, "roundId", "totalResponses", "items");
            if (path.EndsWith("/statistics", StringComparison.Ordinal)) return O(new() { ["roundId"] = S(), ["totalResponses"] = N(), ["byCity"] = A(EvaluationBucket()), ["byApp"] = A(EvaluationBucket()), ["bySupervisor"] = A(EvaluationBucket()), ["byNationality"] = A(EvaluationBucket()), ["factorAverages"] = A(EvaluationBucket()), ["dailyTrend"] = A(EvaluationBucket()) }, "roundId", "totalResponses", "byCity", "byApp", "bySupervisor", "byNationality", "factorAverages", "dailyTrend");
            if (path.Contains("/scorecards/", StringComparison.Ordinal) && path.EndsWith("/export", StringComparison.Ordinal)) return null;
            if (path.EndsWith("/scorecards/supervisors", StringComparison.Ordinal)) return EvaluationSupervisorScorecard();
            if (path.EndsWith("/scorecards/apps", StringComparison.Ordinal)) return EvaluationAppScorecard();
            if (path.EndsWith("/scorecards/finance", StringComparison.Ordinal)) return EvaluationFinanceScorecard();
            if (path.EndsWith("/scorecards/heatmap", StringComparison.Ordinal)) return EvaluationHeatmap();
            if (path.EndsWith("/scorecards/city-app", StringComparison.Ordinal)) return EvaluationCityAppMatrix();
            if (path.EndsWith("/export", StringComparison.Ordinal)) return null;
            if (System.Text.RegularExpressions.Regex.IsMatch(path, @"/rounds/\{[^}]+\}/sections/\{[^}]+\}/staff/\{[^}]+\}$")) return EvaluationRound();
            if (get && path.EndsWith("/rounds", StringComparison.Ordinal)) return A(EvaluationRound());
            if (method == "DELETE" && System.Text.RegularExpressions.Regex.IsMatch(path, @"/rounds/\{[^}]+\}$"))
                return O(new() { ["repliesDeleted"] = N(), ["attachmentsDeleted"] = N(), ["complaintsDeleted"] = N() }, "repliesDeleted", "attachmentsDeleted", "complaintsDeleted");
            return EvaluationRound();
        }
        if (path == "/api/rider-mobile/evaluations") return A(O(new() { ["id"] = S(), ["title"] = S(), ["token"] = S(), ["schema"] = S() }, "id", "title", "token", "schema"));
        if (path == "/api/rider-mobile/evaluations/{token}/launch") return O(new() { ["url"] = S() }, "url");
        if (path == "/api/public/evaluations/{token}/session") return O(new() { ["accessToken"] = S(), ["iqama"] = S(), ["expiresAt"] = S() }, "accessToken", "iqama", "expiresAt");
        if (path is "/api/public/evaluations/{token}" or "/api/rider-mobile/evaluations/{token}")
            return get ? EvaluationSchema() : O(new() { ["receipt"] = S(), ["allowedActions"] = A(S()), ["editPolicyPending"] = B() }, "receipt", "allowedActions", "editPolicyPending");
        return null;
    }
}
