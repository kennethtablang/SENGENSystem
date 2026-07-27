import { useMemo, useState } from 'react';
import provinces from './psgc/provinces.json';
import cities from './psgc/cities.json';

// Cascading Philippine address selection (PSGC): Province → City/Municipality → Barangay,
// then the plain house/street line. Data is bundled with the app (no external API), and the
// ~42k-row barangay set is split one file per province and dynamically imported, so only the
// chosen province's barangays are ever downloaded. Names are stored UPPERCASE to match the rest
// of the Student Information Sheet (FR-AUTH-03).
const barangayLoaders = import.meta.glob('./psgc/barangays/*.json');

const up = (s) => (s || '').toUpperCase();

export default function AddressPicker({ form, setForm, err }) {
    // Codes drive the cascade; the form stores the human-readable (uppercased) names.
    const [provinceCode, setProvinceCode] = useState('');
    const [cityCode, setCityCode] = useState('');
    const [barangays, setBarangays] = useState([]);
    const [loadingBrgy, setLoadingBrgy] = useState(false);

    const provinceCities = useMemo(
        () => (provinceCode ? cities.filter(c => c.prov === provinceCode) : []),
        [provinceCode]
    );

    function selectProvince(e) {
        const code = e.target.value;
        const name = provinces.find(p => p.code === code)?.name ?? '';
        setProvinceCode(code);
        setCityCode('');
        setBarangays([]);
        setForm(prev => ({ ...prev, province: up(name), cityMunicipality: '', barangay: '' }));
    }

    async function selectCity(e) {
        const code = e.target.value;
        const name = provinceCities.find(c => c.code === code)?.name ?? '';
        setCityCode(code);
        setBarangays([]);
        setForm(prev => ({ ...prev, cityMunicipality: up(name), barangay: '' }));
        if (!code) return;

        const loader = barangayLoaders[`./psgc/barangays/${provinceCode}.json`];
        if (!loader) return;
        setLoadingBrgy(true);
        try {
            const mod = await loader();
            const all = mod.default ?? mod;
            setBarangays(all.filter(b => b.city === code));
        } finally {
            setLoadingBrgy(false);
        }
    }

    function selectBarangay(e) {
        const code = e.target.value;
        const name = barangays.find(b => b.code === code)?.name ?? '';
        setForm(prev => ({ ...prev, barangay: up(name) }));
    }

    // House/lot/street and zip are free text — uppercase them the same way the rest of the form does.
    const setText = (field) => (e) =>
        setForm(prev => ({ ...prev, [field]: e.target.value.toUpperCase() }));

    const barangayValue = barangays.find(b => up(b.name) === form.barangay)?.code ?? '';

    return (
        <>
            <div className="field-row">
                <div className="field">
                    <label htmlFor="province">Province *</label>
                    <select id="province" value={provinceCode} onChange={selectProvince} required>
                        <option value="" disabled>Select province…</option>
                        {provinces.map(p => (
                            <option key={p.code} value={p.code}>{up(p.name)}</option>
                        ))}
                    </select>
                    {err('province') && <p className="field-error">{err('province')}</p>}
                </div>
                <div className="field">
                    <label htmlFor="cityMunicipality">City / municipality *</label>
                    <select
                        id="cityMunicipality"
                        value={cityCode}
                        onChange={selectCity}
                        required
                        disabled={!provinceCode}
                    >
                        <option value="" disabled>
                            {provinceCode ? 'Select city / municipality…' : 'Select a province first'}
                        </option>
                        {provinceCities.map(c => (
                            <option key={c.code} value={c.code}>{up(c.name)}</option>
                        ))}
                    </select>
                    {err('cityMunicipality') && <p className="field-error">{err('cityMunicipality')}</p>}
                </div>
            </div>

            <div className="field-row">
                <div className="field">
                    <label htmlFor="barangay">Barangay *</label>
                    <select
                        id="barangay"
                        value={barangayValue}
                        onChange={selectBarangay}
                        required
                        disabled={!cityCode || loadingBrgy}
                    >
                        <option value="" disabled>
                            {loadingBrgy
                                ? 'Loading barangays…'
                                : cityCode ? 'Select barangay…' : 'Select a city first'}
                        </option>
                        {barangays.map(b => (
                            <option key={b.code} value={b.code}>{up(b.name)}</option>
                        ))}
                    </select>
                    {err('barangay') && <p className="field-error">{err('barangay')}</p>}
                </div>
                <div className="field">
                    <label htmlFor="zipCode">Zip code</label>
                    <input id="zipCode" type="text" value={form.zipCode} onChange={setText('zipCode')} inputMode="numeric" />
                    {err('zipCode') && <p className="field-error">{err('zipCode')}</p>}
                </div>
            </div>

            <div className="field">
                <label htmlFor="addressLine">House / lot / unit no. &amp; street *</label>
                <input id="addressLine" type="text" value={form.addressLine} onChange={setText('addressLine')} required />
                {err('addressLine') && <p className="field-error">{err('addressLine')}</p>}
            </div>
        </>
    );
}
