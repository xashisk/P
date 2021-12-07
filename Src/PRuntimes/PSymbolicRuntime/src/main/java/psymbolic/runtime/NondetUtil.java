package psymbolic.runtime;

import psymbolic.valuesummary.Guard;
import psymbolic.valuesummary.GuardedValue;
import psymbolic.valuesummary.PrimitiveVS;
import psymbolic.valuesummary.util.ValueSummaryUnionFind;

import java.util.*;
import java.util.stream.Collectors;

public class NondetUtil {

    private static int log2(int bits)
    {
        if( bits == 0 )
            return 0; // or throw exception
        return 31 - Integer.numberOfLeadingZeros(bits);
    }

    private static List<Guard> generateAllCombos(List<Guard> bdds) {
        Guard thisGuard = bdds.get(0);
        List<Guard> remaining = bdds.subList(1, bdds.size());
        if (remaining.size() == 0) {
            List<Guard> res = new ArrayList<>();
            res.add(thisGuard);
            res.add(thisGuard.not());
            return res;
        }
        List<Guard> rec = generateAllCombos(remaining);
        List<Guard> res = rec.stream().map(x -> x.and(thisGuard)).collect(Collectors.toList());
        res.addAll(rec.stream().map(x -> x.and(thisGuard.not())).collect(Collectors.toList()));
        return res;
    }

    public static <T> PrimitiveVS<T> getNondetChoiceAlt(List<PrimitiveVS<T>> choices) {
        if (choices.size() == 0) return new PrimitiveVS<>();
        if (choices.size() == 1) return choices.get(0);
        List<PrimitiveVS<T>> results = new ArrayList<>();
        PrimitiveVS<T> empty = choices.get(0).restrict(Guard.constFalse());
        List<Guard> choiceVars = new ArrayList<>();

        int numVars = 1;
        while ((1 << numVars) - choices.size() < 0) {
            numVars++;
        }

        for (int i = 0; i < numVars; i++) {
            choiceVars.add(Guard.newVar());
        }

        List<Guard> choiceConds = generateAllCombos(choiceVars);

        Guard accountedPc = Guard.constFalse();
        for (int i = 0; i < choices.size(); i++) {
            PrimitiveVS<T> choice = choices.get(i).restrict(choiceConds.get(i));
            results.add(choice);
            accountedPc = accountedPc.or(choice.getUniverse());
        }

        Guard residualPc = accountedPc.not();

        int i = 0;
        for (PrimitiveVS<T> choice : choices) {
            if (residualPc.isFalse()) break;
            Guard enabledCond = choice.getUniverse();
            PrimitiveVS<T> guarded = choice.restrict(residualPc);
            results.add(guarded);
            residualPc = residualPc.and(enabledCond.not());
        }
        return empty.merge(results);
    }

    public static <T> PrimitiveVS<T> getNondetChoice(List<PrimitiveVS<T>> choices) {
        if (choices.size() == 0) return new PrimitiveVS<>();
        if (choices.size() == 1) return choices.get(0);
        List<PrimitiveVS<T>> results = new ArrayList<>();
        PrimitiveVS<T> empty = choices.get(0).restrict(Guard.constFalse());
        List<Guard> choiceVars = new ArrayList<>();

        ValueSummaryUnionFind uf = new ValueSummaryUnionFind(choices);
        Collection<Set<PrimitiveVS<T>>> disjoint = uf.getDisjointSets();
        // only need to distinguish between things within disjoint sets
        Map<Set<PrimitiveVS<T>>, Guard> universeMap = uf.getLastUniverseMap();

        int maxSize = 0;
        for (Set<PrimitiveVS<T>> set : disjoint) {
            int size = set.size();
            if (size > maxSize) {
                maxSize = size;
            }
        }

        int numVars = 1;
        while ((1 << numVars) - maxSize < 0) {
            numVars++;
        }

        for (int i = 0; i < numVars; i++) {
            choiceVars.add(Guard.newVar());
        }

        List<Guard> choiceConds = generateAllCombos(choiceVars);

        for (Set<PrimitiveVS<T>> set : disjoint) {
            results.addAll(getIntersectingNondetChoice(new ArrayList<>(set), choiceConds, universeMap.get(set)));
        }

        return empty.merge(results);
    }

    public static <T> List<PrimitiveVS<T>> getIntersectingNondetChoice(List<PrimitiveVS<T>> choices, List<Guard> choiceConds, Guard universe) {
        List<PrimitiveVS<T>> results = new ArrayList<>();
        Guard accountedPc = Guard.constFalse();
        for (int i = 0; i < choices.size(); i++) {
            PrimitiveVS<T> choice = choices.get(i).restrict(choiceConds.get(i));
            results.add(choice);
            accountedPc = accountedPc.or(choice.getUniverse());
        }

        Guard residualPc = accountedPc.not().and(universe);

        for (PrimitiveVS<T> choice : choices) {
            if (residualPc.isFalse()) break;
            Guard enabledCond = choice.getUniverse();
            PrimitiveVS<T> guarded = choice.restrict(residualPc);
            results.add(guarded);
            residualPc = residualPc.and(enabledCond.not());
        }
        return results;
    }

    public static <T> Guard chooseGuard(int n, PrimitiveVS<T> choice) {
        Guard guard = Guard.constFalse();
        if (choice.getGuardedValues().size() <= n) return Guard.constTrue();
        for (GuardedValue<T> guardedValue : (List<GuardedValue<T>>) choice.getGuardedValues()) {
            if (n == 0) break;
            guard = guard.or(guardedValue.getGuard());
            n--;
        }
        return guard;
    }

    public static <T> PrimitiveVS<T> excludeChoice(Guard guard, PrimitiveVS<T> choice) {
        PrimitiveVS<T> newChoice = choice.restrict(guard.not());
        List<GuardedValue<T>> guardedValues = newChoice.getGuardedValues();
        if (guardedValues.size() > 0) {
            newChoice.merge(new PrimitiveVS<>(guardedValues.iterator().next().getValue()).restrict(guard));
        }
        return newChoice;
    }
}
