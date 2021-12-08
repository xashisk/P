package psymbolic.runtime.scheduler;

import psymbolic.valuesummary.*;
import psymbolic.runtime.machine.Machine;
import psymbolic.commandline.PSymConfiguration;

import java.util.ArrayList;
import java.util.List;

public class DPORScheduler extends IterativeBoundedScheduler {

    @Override
    public Schedule getNewSchedule() {
        return new DPORSchedule();
    }

    public DPORScheduler(PSymConfiguration config) {
        super(config);
    }

    // don't use the guards because they may not match
    List<PrimitiveVS<Machine>> toExplore = new ArrayList<>();

    @Override
    public List<PrimitiveVS<Machine>> getNextSenderChoices() {
        List<PrimitiveVS<Machine>> senderChoices = super.getNextSenderChoices();
        if (toExplore.isEmpty()) {
            toExplore = ((DPORSchedule.DPORChoice) getSchedule().getChoice(getDepth())).getToExplore();
        }
        if (!toExplore.isEmpty()) {
          Guard canExplore = Guard.constFalse();
          List<PrimitiveVS<Machine>> newSenderChoices = new ArrayList<>();
          for (PrimitiveVS<Machine> choice : senderChoices) {
             for (GuardedValue<Machine> sender : toExplore.get(0).getGuardedValues()) {
                 canExplore = canExplore.or(choice.symbolicEquals(new PrimitiveVS<>(sender.getValue()), choice.getUniverse()).getGuardFor(true));
             }
             newSenderChoices.add(choice.restrict(canExplore));
          }
          toExplore.remove(0);
          return newSenderChoices;
        }
        return senderChoices;
    }

    @Override
    public void postIterationCleanup() {
        ((DPORSchedule) getSchedule()).buildNextToExplore();
        super.postIterationCleanup();
        ((DPORSchedule) getSchedule()).updateSleepSets();
    }
}
