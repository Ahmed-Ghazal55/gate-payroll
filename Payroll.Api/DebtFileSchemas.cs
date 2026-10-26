namespace Payroll;

public static class DebtFileSchemas
{
    public static readonly string[] CollectionAr=["رقم الإقامة","اسم المندوب / الموظف","المبلغ المحصل","مبلغ الإعفاء الإداري","رمز الصندوق أو البنك","رقم السند / المرجع","ملاحظات التسوية"];
    public static readonly string[] CollectionEn=["iqama","riderName","collectedAmount","waivedAmount","targetVaultCode","receiptRef","notes"];
    public static readonly string[] OpeningAr=["رقم الإقامة (10 أرقام)","اسم المندوب / الموظف","تصنيف المدين (Rider/Employee)","كود البند أو الخدمة (Category)","السجل التجاري الدائن (BW/EX/TR)","نوع القيد (OpeningBalance/Disbursement)","آلية التحمل (RiderFull/SharedSplit/CompanyFull)","إجمالي مبلغ المديونية على المندوب","المسدد سابقاً (إن وجد)","حصة تحمل الشركة (في حال SharedSplit)","القسط الشهري المستقطع","شهر بداية الاستقطاع (YYYY-MM)","الحساب الدائن المقابل (3101 أو رمز الصندوق)","رقم السند / المرجع","ملاحظات البيان"];
    public static readonly string[] OpeningEn=["iqama","riderName","debtorType","category","creditorSponsor","entryMode","allocationMode","totalAmount","priorPaidAmount","companyShareAmount","monthlyInstallment","effectiveFrom","contraAccountCode","sourceRef","notes"];
    public static void Validate(IReadOnlyList<string> headers,bool opening)
    {
        var ar=opening?OpeningAr:CollectionAr;var en=opening?OpeningEn:CollectionEn;
        bool baseMatch=headers.Take(ar.Length).SequenceEqual(ar)||headers.Take(en.Length).SequenceEqual(en);
        bool length=headers.Count==ar.Length||opening&&headers.Count==ar.Length+1&&headers[^1] is "platform" or "المنصة (Platform)";
        J.Need(baseMatch&&length,"أعمدة الملف لا تطابق القالب المعتمد؛ نزّل القالب دون إعادة ترتيب العناوين.");
    }
}
