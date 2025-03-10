import { InputHTMLAttributes } from 'react';
import MaskedInput from 'react-text-mask';

import { Error } from '../../form';
import { Show } from '../../show';
import * as styled from './styled';

export interface ITimeInputProps extends InputHTMLAttributes<HTMLInputElement> {
  label?: string;
  /**
   * Display errors.
   */
  error?: string;
}

/** Component that will enforce the HH:MM:SS time format */
export const TimeInput: React.FC<ITimeInputProps> = ({ label, error, ...rest }) => {
  const formatTime = (value: string) => {
    const chars = value.split('');
    const hours = [/[0-2]/, chars[0] === '2' ? /[0-3]/ : /[0-9]/] as any;

    const minutes = [/[0-5]/, /[0-9]/];
    const seconds = [/[0-5]/, /[0-9]/];

    return hours.concat(':').concat(minutes).concat(':').concat(seconds) as any;
  };
  return (
    <styled.TimeInput {...rest}>
      <Show visible={!!label}>
        <label className={rest.required ? 'required' : ''}>{label}</label>
      </Show>
      <MaskedInput
        role={!!error ? 'alert' : 'none'}
        className="masked-input"
        {...rest}
        mask={formatTime}
      />
      <Error error={error} />
    </styled.TimeInput>
  );
};
