import { ContentTypeName, IIngestTypeModel } from 'hooks';

export const defaultIngestType: IIngestTypeModel = {
  id: 0,
  name: '',
  description: '',
  isEnabled: true,
  sortOrder: 0,
  contentType: ContentTypeName.Snippet,
  autoTranscribe: false,
  disableTranscribe: false,
};
