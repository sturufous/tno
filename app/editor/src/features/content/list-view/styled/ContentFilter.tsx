import styled from 'styled-components';

export const ContentFilter = styled.div`
  position: relative;
  display: flex;
  flex-direction: row;
  margin-bottom: 1em;

  h2 {
    border: 0;
    padding: 0;
    margin-top: 0;
    font-weight: 400;
    font-size: 1em;
  }

  .box {
    margin-top: 0.6em;
    margin-left: 1em;
    border: solid 1px grey;
    border-radius: 0.25em;
    max-width: 50em;
    padding: 1em;
  }

  & > div:first-child {
    max-width: 20em;
    margin-right: 1em;
  }

  & > div:nth-child(2) {
    flex-grow: 2;

    & > div:nth-child(2) {
      display: flex;
      flex-direction: row;
    }
  }

  .box-content {
    display: flex;
    flex-direction: row;
    min-width: 300px;
  }

  .action-filters {
    display: flex;
    flex-direction: row;
  }
`;
