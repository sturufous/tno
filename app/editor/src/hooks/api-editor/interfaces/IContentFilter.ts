import { ContentStatus, ContentTypeName, LogicalOperator } from '..';
import { IPageFilter } from '.';

export interface IContentFilter extends IPageFilter {
  contentType?: ContentTypeName;
  productId?: number;
  otherSource?: string;
  sourceId?: number;
  ownerId?: number;
  userId?: number;
  pageName?: string;
  section?: string;
  status?: ContentStatus;
  includedInCategory?: boolean;
  includeHidden?: boolean;
  createdOn?: string;
  createdStartOn?: string;
  createdEndOn?: string;
  updatedOn?: string;
  updatedStartOn?: string;
  updatedEndOn?: string;
  publishedOn?: string;
  publishedStartOn?: string;
  publishedEndOn?: string;
  actions?: string[];
  productIds?: number[];
  logicalOperator?: LogicalOperator;
  sort?: string[];
}
