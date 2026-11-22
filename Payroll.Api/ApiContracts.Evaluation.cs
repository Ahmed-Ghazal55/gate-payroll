using Microsoft.OpenApi;
namespace Payroll;

public static partial class ApiContracts
{
    static OpenApiSchema EvaluationStaff() => O(new() { ["id"] = S(), ["name"] = S(), ["cities"] = A(S()), ["departments"] = A(S()), ["roles"] = A(S()), ["apps"] = A(S()), ["active"] = B(), ["excluded"] = B(), ["pendingRoleApproval"] = B() }, "id", "name", "cities", "departments", "roles", "apps");
    static OpenApiSchema EvaluationRound() => O(new() { ["id"] = S(), ["title"] = S(), ["status"] = S(), ["kind"] = S(), ["schema"] = S(), ["sharingToken"] = S(), ["staff"] = A(EvaluationStaff()), ["cities"] = A(S()), ["apps"] = A(S()), ["createdAt"] = S(), ["updatedAt"] = S(), ["schemaVersion"] = N(), ["allowedActions"] = A(S()) }, "id", "title", "status", "sharingToken", "staff", "cities", "apps", "allowedActions");
    static OpenApiSchema EvaluationLabel() => O(new() { ["id"] = S(), ["labelAr"] = S(), ["labelEn"] = S(), ["labelUr"] = S() }, "id", "labelAr", "labelEn", "labelUr");
    static OpenApiSchema EvaluationSchema() => O(new()
    {
        ["schema"] = S(), ["roundId"] = S(), ["title"] = S(), ["noticeAr"] = S(), ["noticeEn"] = S(), ["noticeUr"] = S(),
        ["cities"] = A(S()), ["apps"] = A(S()), ["staff"] = A(EvaluationStaff()), ["identityFields"] = A(EvaluationLabel()),
        ["staffRatingMin"] = N(), ["staffRatingMax"] = N(), ["appRatingMin"] = N(), ["appRatingMax"] = N(),
        ["staffDimensions"] = A(EvaluationLabel()), ["staffReasons"] = A(EvaluationLabel()), ["appReasons"] = A(EvaluationLabel()), ["factors"] = A(EvaluationLabel()),
        ["factorMode"] = S(), ["editPolicyPending"] = B(), ["uploadsPolicyPending"] = B(), ["allowedActions"] = A(S()),
        ["attachmentLimits"] = O(new() { ["imagesPerCard"] = N(), ["voicePerCard"] = N(), ["voiceSeconds"] = N(), ["replyBytes"] = N() })
    }, "schema", "roundId", "title", "staff", "cities", "apps", "staffDimensions", "staffReasons", "appReasons", "factors", "allowedActions");
    static OpenApiSchema EvaluationCard() => O(new() { ["staffId"] = S(), ["app"] = S(), ["reason"] = S(), ["ratings"] = O(EvaluationSurvey.StaffDimensions.ToDictionary(d => d, _ => (IOpenApiSchema)N())), ["overall"] = N(), ["factorRatings"] = O(EvaluationSurvey.Factors.ToDictionary(d => d, _ => (IOpenApiSchema)N())), ["notes"] = S(), ["complaint"] = B(), ["complaintDetails"] = S(), ["attachments"] = A(Free()) });
    static OpenApiSchema EvaluationReply() => O(new() { ["id"] = S(), ["roundId"] = S(), ["schemaVersion"] = N(), ["iqama"] = S(), ["name"] = S(), ["city"] = S(), ["nationality"] = S(), ["phone"] = S(), ["workApps"] = A(S()), ["companyId"] = S(), ["identityVerified"] = B(), ["staffAnswers"] = A(EvaluationCard()), ["appAnswers"] = A(EvaluationCard()), ["createdAt"] = S() }, "id", "roundId", "iqama", "name", "city", "nationality", "workApps", "staffAnswers", "appAnswers", "createdAt");
    static OpenApiSchema EvaluationBucket() => O(new()
    {
        ["key"] = S(), ["app"] = S(), ["count"] = N(), ["responseCount"] = N(), ["complaintCount"] = N(), ["ratedCount"] = N(), ["skippedCount"] = N(),
        ["average"] = new OpenApiSchema { Type = JsonSchemaType.Number | JsonSchemaType.Null },
        ["staffRatedCount"] = N(), ["appRatedCount"] = N(), ["staffAverage"] = new OpenApiSchema { Type = JsonSchemaType.Number | JsonSchemaType.Null }, ["appAverage"] = new OpenApiSchema { Type = JsonSchemaType.Number | JsonSchemaType.Null },
        ["reasons"] = A(O(new() { ["key"] = S(), ["count"] = N() }, "key", "count")),
        ["dimensions"] = A(O(new() { ["key"] = S(), ["count"] = N(), ["average"] = new OpenApiSchema { Type = JsonSchemaType.Number | JsonSchemaType.Null } }, "key", "count", "average")),
        ["distribution"] = A(O(new() { ["stars"] = N(), ["count"] = N() }, "stars", "count"))
    }, "key");
    static OpenApiSchema? EvaluationResponse(string method, string path)
    {
        bool get = method == "GET";
        if (path.StartsWith("/api/forms/evaluations/", StringComparison.Ordinal))
        {
            if (path.EndsWith("/staff/import", StringComparison.Ordinal)) return O(new() { ["imported"] = N(), ["excluded"] = N() }, "imported", "excluded");
            if (path.EndsWith("/staff", StringComparison.Ordinal)) return A(EvaluationStaff());
            if (path.EndsWith("/responses", StringComparison.Ordinal)) return Page(EvaluationReply());
            if (path.EndsWith("/statistics/{dimension}", StringComparison.Ordinal)) return O(new() { ["roundId"] = S(), ["totalResponses"] = N(), ["items"] = A(EvaluationBucket()) }, "roundId", "totalResponses", "items");
            if (path.EndsWith("/statistics", StringComparison.Ordinal)) return O(new() { ["roundId"] = S(), ["totalResponses"] = N(), ["byCity"] = A(EvaluationBucket()), ["byApp"] = A(EvaluationBucket()), ["bySupervisor"] = A(EvaluationBucket()), ["byNationality"] = A(EvaluationBucket()), ["factorAverages"] = A(EvaluationBucket()), ["dailyTrend"] = A(EvaluationBucket()) }, "roundId", "totalResponses", "byCity", "byApp", "bySupervisor", "byNationality", "factorAverages", "dailyTrend");
            if (path.EndsWith("/export", StringComparison.Ordinal)) return null;
            return get && path.EndsWith("/rounds", StringComparison.Ordinal) ? A(EvaluationRound()) : EvaluationRound();
        }
        if (path == "/api/rider-mobile/evaluations") return A(O(new() { ["id"] = S(), ["title"] = S(), ["token"] = S(), ["schema"] = S() }, "id", "title", "token", "schema"));
        if (path == "/api/rider-mobile/evaluations/{token}/launch") return O(new() { ["url"] = S() }, "url");
        if (path == "/api/public/evaluations/{token}/session") return O(new() { ["accessToken"] = S(), ["iqama"] = S(), ["expiresAt"] = S() }, "accessToken", "iqama", "expiresAt");
        if (path is "/api/public/evaluations/{token}" or "/api/rider-mobile/evaluations/{token}")
            return get ? EvaluationSchema() : O(new() { ["receipt"] = S(), ["allowedActions"] = A(S()), ["editPolicyPending"] = B() }, "receipt", "allowedActions", "editPolicyPending");
        return null;
    }
}
