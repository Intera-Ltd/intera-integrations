export * from '@intera-integrations/shared/contract-types';

export interface IZohoInventoryConfig {
  apiBaseUrl: string;
  serviceToken: string;
}

export interface IZohoInventoryApiResponse<T> {
  ok: boolean;
  data: T | null;
  error: string | null;
}

export interface IZohoSchema {
  custom_fields: Record<string, Array<{ name: string; label: string; type: string }>>;
}

export interface IZohoOrganization {
  organization_id: string | null;
  name: string | null;
  currency_code: string | null;
  time_zone: string | null;
  country: string | null;
  is_active: boolean | null;
}
