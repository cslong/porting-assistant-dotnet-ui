import { Box, Button, Modal, Spinner } from "@awsui/components-react";
import React, { useMemo } from "react";
import { useForm } from "react-hook-form";
import { useDispatch } from "react-redux";

import { PortingLocation } from "../../models/porting";
import { SolutionDetails } from "../../models/solution";
import { setPortingLocation } from "../../store/actions/porting";
import { LocationSection } from "./LocationSection";

interface Props {
  solution: SolutionDetails;
  visible: boolean;
  onDismiss: () => void;
  onSubmit: () => void;
}

const PortConfigurationModalInternal: React.FC<Props> = ({ solution, visible, onDismiss, onSubmit }) => {
  const { control, handleSubmit, errors, formState, setValue, watch, setError } = useForm({
    mode: "onChange"
  });
  const { isSubmitting } = formState;
  const dispatch = useDispatch();

  const addPorting = useMemo(
    () => async (data: Record<string, any>) => {
      const portingLocation: PortingLocation = {
        type: data.portingLocation.value,
        workingDirectory: data.path
      };
      switch (portingLocation.type) {
        case "copy":
          try {
              await window.porting.copyDirectory(solution.solutionFilePath, portingLocation.workingDirectory);
          } catch (err) {
            setError("path", "error", `Unable to copy solution to directory. Error: ${err}`);
            return false;
          }
          break;
      }
      dispatch(setPortingLocation({ solutionPath: solution.solutionFilePath, portingLocation }));
    },
    [dispatch, setError, solution]
  );

  return (
    <Modal
      visible={visible}
      header="How would you like to save ported projects?"
      footer={
        <Box variant="span" float="right">
          {isSubmitting && <Spinner />}
          <Button variant="link" formAction="none" onClick={onDismiss} disabled={isSubmitting}>
            Cancel
          </Button>
          <Button
            id="save-button"
            variant="primary"
            formAction="submit"
            onClick={e =>
              handleSubmit(async data => {
                await addPorting(data);
                onSubmit();
              })()
            }
            disabled={isSubmitting}
          >
            Save
          </Button>
        </Box>
      }
      onDismiss={onDismiss}
    >
      <LocationSection
        isSubmitting={isSubmitting}
        control={control}
        setValue={setValue}
        errors={errors}
        watch={watch}
      />
    </Modal>
  );
};

export const PortConfigurationModal = React.memo(PortConfigurationModalInternal);
