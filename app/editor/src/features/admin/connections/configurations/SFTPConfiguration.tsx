import { useFormikContext } from 'formik';
import { IConnectionModel } from 'hooks';
import { Col, FormikText, Row } from 'tno-core';

export const SFTPConfiguration = () => {
  const { values } = useFormikContext<IConnectionModel>();

  return (
    <div>
      <Row>
        <Col flex="1 1 0">
          <FormikText
            label="Hostname"
            name="configuration.hostname"
            value={values.configuration?.hostname}
          />
        </Col>
        <Col flex="1 1 0">
          <FormikText
            label="Port"
            name="configuration.port"
            value={values.configuration?.port}
            type="number"
            placeholder="22"
          />
        </Col>
        <Col flex="1 1 0">
          <FormikText label="Path" name="configuration.path" value={values.configuration?.path} />
        </Col>
      </Row>
      <Row>
        <Col flex="1 1 0">
          <FormikText
            label="Username"
            name="configuration.username"
            value={values.configuration?.username}
            autoComplete="off"
          />
        </Col>
        <Col flex="1 1 0">
          <FormikText
            label="Password"
            name="configuration.password"
            value={values.configuration?.password}
            type="password"
            autoComplete="new-password"
          />
        </Col>
      </Row>
    </div>
  );
};
