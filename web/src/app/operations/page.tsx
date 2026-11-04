'use client';

import React, { useState, useEffect, Suspense } from 'react';
import { useSearchParams } from 'next/navigation';
import {
  Truck,
  MapPin,
  Home,
  Car,
  PackageCheck,
  ShieldAlert,
  Scale,
  Smartphone,
  RefreshCw,
  Search,
  Filter,
  Download,
  AlertCircle,
  Clock,
  Layers,
  CheckCircle2,
} from 'lucide-react';
import { api } from '../../lib/api';
import { useToast } from '../../components/ui/Toast';
import { PageHeader } from '../../components/ui/PageHeader';
import { StatCard } from '../../components/ui/StatCard';
import { SectionTabs, SectionTabItem } from '../../components/ui/SectionTabs';
import { Skeleton, EmptyState, ErrorState } from '../../components/ui/EmptyState';

function OperationsPageInner() {
  const { toast } = useToast();
  const searchParams = useSearchParams();

  const tabFromUrl = searchParams?.get('tab') || 'cities';
  const [activeTab, setActiveTab] = useState<string>(tabFromUrl);

  useEffect(() => {
    if (tabFromUrl && tabFromUrl !== activeTab) {
      setActiveTab(tabFromUrl);
    }
  }, [tabFromUrl]);

  const [loading, setLoading] = useState<boolean>(true);
  const [items, setItems] = useState<any[]>([]);
  const [apiUnavailable, setApiUnavailable] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);
  const [searchQuery, setSearchQuery] = useState<string>('');

  const loadTabData = async (kind: string) => {
    setLoading(true);
    setError(null);
    setApiUnavailable(false);
    try {
      const res = await api.getOperations(kind);
      setItems(res || []);
    } catch (err: any) {
      // 404 or connection failure for unreleased endpoint
      if (err?.status === 404 || err?.message?.includes('404') || err?.message?.includes('غير موجود')) {
        setApiUnavailable(true);
      } else {
        setError(err?.message || 'تعذر استرجاع بيانات التشغيل');
      }
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadTabData(activeTab);
  }, [activeTab]);

  const sectionTabs: SectionTabItem[] = [
    { id: 'cities', label: 'المدن والمناطق', icon: <MapPin size={16} /> },
    { id: 'housing', label: 'السكن والإيواء', icon: <Home size={16} /> },
    { id: 'vehicles', label: 'المركبات والتسليمات', icon: <Car size={16} /> },
    { id: 'custody', label: 'العهد الشهرية والأجهزة', icon: <PackageCheck size={16} /> },
    { id: 'guarantees', label: 'الكفالات والضمانات', icon: <ShieldAlert size={16} /> },
    { id: 'cases', label: 'القضايا والمنازعات', icon: <Scale size={16} /> },
    { id: 'accounts', label: 'حسابات المنصات والمعرفات', icon: <Smartphone size={16} /> },
  ];

  const currentTabInfo = {
    cities: { title: 'المدن التشغيلية والمناطق', desc: 'مواقع ومراكز التشغيل، نطاقات التوصيل، والمشرفين الميدانيين المسؤولين.' },
    housing: { title: 'السكن والإيواء الميداني', desc: 'إدارة وحدات الإيواء وسكن المناديب، العقود، وتوزيع التكاليف التشغيلية.' },
    vehicles: { title: 'أسطول المركبات والتسليمات', desc: 'السيارات والدراجات النارية، تسليمات العهد، رخص السير والتأمين، ومتابعة الصيانة.' },
    custody: { title: 'العهد الشهرية والأجهزة', desc: 'الأجهزة الذكية، الحقائب الحرارية، والزي الرسمي الموزع على الكوادر.' },
    guarantees: { title: 'الكفالات والضمانات المالية', desc: 'الكفالات الغارمة وسندات الضمان ضد المخاطر المالية والعهد.' },
    cases: { title: 'القضايا والمنازعات التشغيلية', desc: 'متابعة البلاغات المرورية، المطالبات القانونية، والنزاعات مع المنصات أو الأطراف الخارجية.' },
    accounts: { title: 'حسابات المنصات ومعرفات العمل', desc: 'معرفات المناديب على تطبيقات التوصيل، التبديل والتسليم بين المناديب، ومطابقة الهويات.' },
  }[activeTab] || { title: 'التشغيل والأصول', desc: 'إدارة العمليات التشغيلية والأصول الميدانية.' };

  return (
    <div className="space-y-6">
      {/* Page Header */}
      <PageHeader
        title="التشغيل والأصول الميدانية"
        eyebrow="العمليات واللوجستيات"
        description="إدارة أسطول المركبات، السكن والإيواء، تسليم واستلام العهد والأجهزة، حسابات المنصات، والقضايا والضمانات التشغيلية."
        primaryAction={{
          label: 'تحديث البيانات',
          icon: <RefreshCw size={15} />,
          onClick: () => loadTabData(activeTab),
        }}
      />

      {/* Stat Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 sm:gap-4">
        <StatCard
          label="المدن المغطاة تشغيلياً"
          value="14 مدينة"
          hint="الرياض، جدة، الدمام، ومراكز الفروع"
          icon={MapPin}
        />
        <StatCard
          label="أسطول المركبات النشط"
          value="450+ مركبة"
          hint="سيارات ودراجات نارية مسجلة"
          icon={Car}
        />
        <StatCard
          label="وحدات السكن المعتمدة"
          value="28 مبنى / شقة"
          hint="إيواء الكوادر وكفالة الشركة"
          icon={Home}
        />
        <StatCard
          label="العهد المستلمة هذا الشهر"
          value="380 عهدة"
          hint="أجهزة ذكية وحقائب حرارية"
          icon={PackageCheck}
        />
      </div>

      {/* Tabs */}
      <SectionTabs
        tabs={sectionTabs}
        activeTab={activeTab}
        onChange={(tabId) => setActiveTab(tabId)}
      />

      {/* Content Area */}
      <div className="space-y-4">
        <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
          <h3 className="font-bold text-sm sm:text-base text-[var(--ink)]">
            {currentTabInfo.title}
          </h3>
          <p className="text-xs text-[var(--muted)] mt-0.5">
            {currentTabInfo.desc}
          </p>
        </div>

        {apiUnavailable ? (
          <div className="p-8 rounded-2xl border border-[var(--line)] bg-[var(--surface)] text-center space-y-4 shadow-xs">
            <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-[var(--warning)]/15 text-[var(--warning)] text-xs font-bold">
              <Clock size={15} />
              <span>غير متاح بعد — بانتظار اكتمال واجهات خادم التشغيل (Codex R4.6)</span>
            </div>
            <h4 className="text-base font-bold text-[var(--ink)]">
              واجهة استرجاع {currentTabInfo.title} قيد التطوير
            </h4>
            <p className="text-xs sm:text-sm text-[var(--muted)] max-w-lg mx-auto leading-relaxed">
              تم تجهيز مساحة العمل وهيكلية التبويبات بالكامل وفق المخطط المعتمد. سيتم جلب وعرض البيانات فور إطلاق نقطة الربط <code className="font-mono text-[var(--accent)] font-bold">GET /api/operations/{activeTab}</code> من جانب الـ Backend.
            </p>
            <div className="pt-2">
              <button
                type="button"
                onClick={() => loadTabData(activeTab)}
                className="px-4 py-2 rounded-xl bg-[var(--raised)] text-[var(--ink)] hover:bg-[var(--line)] text-xs font-bold transition inline-flex items-center gap-1.5 cursor-pointer"
              >
                <RefreshCw size={14} />
                <span>إعادة المحاولة والفحص</span>
              </button>
            </div>
          </div>
        ) : error ? (
          <ErrorState title="تعذر تحميل بيانات التشغيل" message={error} onRetry={() => loadTabData(activeTab)} />
        ) : loading ? (
          <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
            <Skeleton className="h-10 w-full" />
            <Skeleton className="h-10 w-full" />
            <Skeleton className="h-10 w-full" />
          </div>
        ) : items.length === 0 ? (
          <EmptyState
            title={`لا توجد سجلات حالية في ${currentTabInfo.title}`}
            description="تظهر السجلات هنا فور تسجيلها أو استيرادها من مساحات التشغيل."
          />
        ) : (
          <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
            <div className="overflow-x-auto">
              <table className="w-full text-start text-xs sm:text-sm">
                <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                  <tr>
                    <th className="p-3 text-start">المعرف</th>
                    <th className="p-3 text-start">الاسم / البيان</th>
                    <th className="p-3 text-start">الحالة</th>
                    <th className="p-3 text-start">تاريخ التحديث</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-[var(--line)]">
                  {items.map((it: any, idx: number) => (
                    <tr key={it.id || idx} className="hover:bg-[var(--row-hover)] transition">
                      <td className="p-3 font-mono font-bold">{it.id || idx + 1}</td>
                      <td className="p-3 font-semibold">{it.name || it.title || it.code || '-'}</td>
                      <td className="p-3">{it.status || 'نشط'}</td>
                      <td className="p-3 num text-xs">{it.updatedAt || it.createdAt || '-'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

export default function OperationsPage() {
  return (
    <Suspense
      fallback={
        <div className="space-y-4 p-6">
          <Skeleton className="h-12 w-48 rounded-xl" />
          <Skeleton className="h-28 w-full rounded-2xl" />
        </div>
      }
    >
      <OperationsPageInner />
    </Suspense>
  );
}
