import { useFormikContext } from 'formik';
import { IContentModel } from 'hooks';
import React from 'react';
import { Col, Error, Row } from 'tno-core';

import * as styled from './styled';

export interface IToningGroupProps {
  fieldName: keyof IContentModel;
}

export const ToningGroup: React.FC<IToningGroupProps> = ({ fieldName }) => {
  const toningOptions = [-5, -4, -3, -2, -1, 0, 1, 2, 3, 4, 5];
  const { values, setFieldValue, errors, touched } = useFormikContext<IContentModel>();

  const [active, setActive] = React.useState<number>();
  React.useEffect(() => {
    if (values.tonePools?.length && values.tonePools[0].value) setActive(values.tonePools[0].value);
  }, [values.tonePools]);

  const determineIndicator = (option: number) => {
    if (option === 5) {
      return (
        <img alt={option.toString()} src={`${process.env.PUBLIC_URL}/assets/face-grin-wide.svg`} />
      );
    } else if (option === 0) {
      return <img alt={option.toString()} src={`${process.env.PUBLIC_URL}/assets/face-meh.svg`} />;
    } else if (option === -5) {
      return (
        <img alt={option.toString()} src={`${process.env.PUBLIC_URL}/assets/face-frown-open.svg`} />
      );
    } else {
      return <span className="blank">&nbsp;</span>;
    }
  };

  return (
    <styled.ToningGroup>
      <label>Toning</label>
      <Row>
        {' '}
        {toningOptions.map((option) => (
          <Col>
            {determineIndicator(option)}
            <button
              className={active === option ? 'active' : ''}
              type="button"
              key={option}
              onClick={() => {
                setActive(option);
                setFieldValue('tonePool', option);
                setFieldValue('tone', option);
              }}
            >
              {option}
            </button>
          </Col>
        ))}
      </Row>
      <Error error={!!fieldName && touched[fieldName] ? (errors[fieldName] as string) : ''} />
    </styled.ToningGroup>
  );
};
