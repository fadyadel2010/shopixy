export interface BostaReference { type: string; id: string; name: string; parentId: string | null; dropOffAvailable: boolean }
export interface BostaNativeEvidence { city: BostaReference; district: BostaReference; observedAtUtc: string }
export function loadBostaNativeGeography(accountId: string, cityId?: string): Promise<{ accountId: string; environment: string; places: BostaReference[] }>;
export function validateBostaNativeDestination(accountId: string, cityId: string, districtId: string): Promise<{ accountId: string; validated: boolean; serviceable: boolean }>;
export function saveBostaNativeDestination(accountId: string, shipmentId: string, cityId: string, districtId: string): Promise<{ evidence: BostaNativeEvidence }>;
export function readBostaNativeDestination(shipmentId: string): Promise<{ accountId: string; evidence: BostaNativeEvidence | null; canSelect: boolean }>;
