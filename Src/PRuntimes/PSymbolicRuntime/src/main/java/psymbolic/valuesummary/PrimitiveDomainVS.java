package psymbolic.valuesummary;

import psymbolic.valuesummary.ai.Domain;
import psymbolic.valuesummary.ai.DomainManager;

import java.util.*;
import java.util.function.BiFunction;
import java.util.function.Function;
import java.util.stream.Collectors;

/**
 * Represents a primitive value summary (Boolean, Integer, Float, String)
 * @param <T> Type of value stored in the primitive value summary
 */
public class PrimitiveVS<T> implements ValueSummary<PrimitiveVS<T>> {
    /**
     * A primitive value is a collection of guarded values
     *
     * The guards on these values *must* be mutually exclusive.
     * In other words, for any two 'value1', 'value2' of type T, the following must be identically false:
     *
     *      and(guardedValues.get(value1), guardedValues.get(value2))
     *
     *  The map 'guardedValues' should never be modified.
     */
    private final Map<Domain<T>, Guard> guardedValues;

    /** Cached list of guarded values */
    private List<GuardedValue<Domain<T>>> guardedValuesList;
    /** Cached set of values */
    private Set<Domain<T>> values = null;

    /** Cached universe */
    private Guard universe = null;

    /** Get all the different possible guarded values */
    public List<GuardedValue<Domain<T>>> getGuardedValues() {
        if (guardedValuesList == null)
            guardedValuesList = guardedValues.entrySet().stream()
                    .map(x -> new GuardedValue<Domain<T>>(x.getKey(), x.getValue())).collect(Collectors.toList());
        return guardedValuesList;
    }

    @Override
    public Guard getUniverse() {
        if(universe == null)
            universe = Guard.orMany(new ArrayList<>(guardedValues.values()));
        return universe;
    }

    public Set<Domain<T>> getValues() {
        if(values == null)
            values = guardedValues.keySet();
        return values;
    }

    /**
     * Create a PrimitiveVS with the largest possible universe (restrict = true) containing only the specified value
     *
     * @param value A primitive value summary containing the passed value under the `true` restrict
     */
    public PrimitiveVS(T value) {
        this.guardedValues = Collections.singletonMap(DomainManager.create(value), Guard.constTrue());
    }

    public PrimitiveVS(Domain<T> value) {
        this.guardedValues = Collections.singletonMap(value, Guard.constTrue());
    }

    /**
     * Create a value summary with the given guarded values
     * Caution: The caller must take care to ensure that the guards on the provided values are mutually exclusive.
     */
    public PrimitiveVS(Map<Domain<T>, Guard> guardedValues) {
        this.guardedValues = new HashMap<>();
        for (Map.Entry<Domain<T>, Guard> entry : guardedValues.entrySet()) {
            if (!entry.getValue().isFalse()) {
                this.guardedValues.put(entry.getKey(), entry.getValue());
            }
        }
    }

    /** Copy constructor for PrimitiveVS
     *
     * @param old The PrimVS to copy
     */
    public PrimitiveVS(PrimitiveVS<T> old) {
        this(old.guardedValues);
    }

    /** Make an empty PrimVS */
    public PrimitiveVS() { this(new HashMap<>()); }



    /** Check if the provided value is a possibility
     *
     * @param value The provided value
     * @return Whether or not the provided value is a possibility
     */
    public boolean hasValue(Domain<T> value) {
        return guardedValues.containsKey(value);
    }

    public boolean hasValue(T value) {
        return !this.getGuardFor(value).isFalse();
    }

    /**
     * Get the restrict for a given value
     *
     * @param value The value for which the restrict should be gotten
     * @return The restrict for the provided value (false if the value does not exist in the VS)
     */
    public Guard getGuardFor(Domain<T> value) {
        return guardedValues.getOrDefault(value, Guard.constFalse());
    }

    public Guard getGuardFor(T value) {
        Guard res = Guard.constFalse();
        for (Map.Entry<Domain<T>, Guard> entry : guardedValues.entrySet()) {
            if (entry.getKey().contains(value)) {
                res = res.and(entry.getValue());
            }
        }
        return res;
    }
    /**
     * Apply the function `func` to each guarded value of type T in the Value Summary and return a primitive value summary with values of type U
     * @param func Function to be applied
     * @param <U> Type of the values in the resultant primitive value summary
     * @return A primitive value summary with values of type U
     */
    public <U> PrimitiveVS<U> apply(Function<T, U> func) {
        final Map<Domain<U>, Guard> results = new HashMap<>();

        for (GuardedValue<Domain<T>> guardedValue : getGuardedValues()) {
            final Domain<U> mapped = DomainManager.apply(func, guardedValue.getValue());
            results.merge(mapped, guardedValue.getGuard(), Guard::or);
        }

        return new PrimitiveVS<U>(results);
    }

    /**
     * Remove the provided Primitive VS values from the set of values
     *
     * @param rm The PrimitiveVS values to remove from the current value summary
     * @return The PrimitiveVS after removal of values
     */
    @Deprecated
    public PrimitiveVS<T> remove(PrimitiveVS<T> rm) {
        Guard guardToRemove = Guard.constFalse();
        for (GuardedValue<Domain<T>> guardedValue : rm.getGuardedValues()) {
            guardToRemove = guardToRemove.or(this.restrict(guardedValue.getGuard()).getGuardFor(guardedValue.getValue()));
        }
        return this.restrict(guardToRemove.not());
    }

    public <U, V> PrimitiveVS<V>
    apply(PrimitiveVS<U> summary2, BiFunction<T, U, V> function) {
        final Map<Domain<V>, Guard> results = new HashMap<>();

        for (GuardedValue<Domain<T>> val1 : this.getGuardedValues()) {
            for (GuardedValue<Domain<U>> val2: summary2.getGuardedValues()) {
                final Guard combinedGuard = val1.getGuard().and(val2.getGuard());
                if (combinedGuard.isFalse()) {
                    continue;
                }
                final Domain<V> mapped = DomainManager.apply(function, val1.getValue(), val2.getValue());
                results.merge(mapped, combinedGuard, Guard::or);
            }
        }

        return new PrimitiveVS<>(results);
    }


    public <Target> PrimitiveVS<Target> apply(
        PrimitiveVS<Target> mergeWith,
        Function<T, Target> function
    ) {
        final List<PrimitiveVS<Target>> toMerge = new ArrayList<>();

        for (GuardedValue<Domain<T>> guardedValue : getGuardedValues()) {
            final Domain<Target> mapped = DomainManager.apply(function, guardedValue.getValue());
            toMerge.add(new PrimitiveVS<>(mapped).restrict(guardedValue.getGuard()));
        }

        return mergeWith.merge(toMerge);
    }

    @Override
    public boolean isEmptyVS() {
        return guardedValues.isEmpty();
    }

    @Override
    public PrimitiveVS<T> restrict(Guard guard) {
        if(guard.equals(getUniverse()))
            return new PrimitiveVS<>(this);

        final Map<Domain<T>, Guard> result = new HashMap<>();

        for (Map.Entry<Domain<T>, Guard> entry : guardedValues.entrySet()) {
            final Guard newEntryGuard = entry.getValue().and(guard);
            if (!newEntryGuard.isFalse()) {
                result.put(entry.getKey(), newEntryGuard);
            }
        }
        return new PrimitiveVS<>(result);
    }

    @Override
    public PrimitiveVS<T> updateUnderGuard(Guard guard, PrimitiveVS<T> updateVal) {
        return this.restrict(guard.not()).merge(Collections.singletonList(updateVal.restrict(guard)));
    }


    @Override
    public PrimitiveVS<T> merge(Iterable<PrimitiveVS<T>> summaries) {
        Map<Domain<T>, Guard> result = new HashMap<>(guardedValues);

        for (PrimitiveVS<T> summary : summaries) {
            for (Map.Entry<Domain<T>, Guard> entry : summary.guardedValues.entrySet()) {
                Map.Entry<Domain<T>, Guard> toJoinWith = null;
                for (Map.Entry<Domain<T>, Guard> entry2 : result.entrySet()) {
                    // assumes that canJoin is transitive, so that there is at most one entry that could be joined with
                    if (DomainManager.canJoin(entry.getKey(), entry2.getKey())) {
                        toJoinWith = entry2;
                    }
                }
                if (toJoinWith != null) {
                    result.remove(toJoinWith.getKey());
                    result.put((Domain<T>) DomainManager.join(entry.getKey(), toJoinWith.getKey()), entry.getValue().or(toJoinWith.getValue()));
                }
                result.merge(entry.getKey(), entry.getValue(), Guard::or);
            }
        }

        return new PrimitiveVS<>(result);
    }

    @Override
    public PrimitiveVS<T> merge(PrimitiveVS<T> summary) {
        return merge(Collections.singletonList(summary));
    }

    @Override
    public PrimitiveVS<Boolean> symbolicEquals(PrimitiveVS<T> cmp, Guard pc) {
        Guard equalCond = Guard.constFalse();
        for (Map.Entry<Domain<T>, Guard> entry : this.guardedValues.entrySet()) {
            if (cmp.guardedValues.containsKey(entry.getKey())) {
                equalCond = equalCond.or(entry.getValue().and(cmp.guardedValues.get(entry.getKey())));
            }
        }
        equalCond = equalCond.or(getUniverse().and(cmp.getUniverse()).not());
        return BooleanVS.trueUnderGuard(pc.and(equalCond));
    }

    @Override
    public String toString() {
        return getValues().toString();
    }

}
