package psymbolic.valuesummary.ai;

import psymbolic.runtime.Event;

import java.util.function.Function;
import java.util.function.BiFunction;

public class DomainManager {

    public static Disjunctive create(Object o) {
        return new Disjunctive(o);
    }

    public static boolean canJoin(Object o1, Object o2) {
        if (o1 instanceof Domain && o2 instanceof Domain) {
            return ((Domain) o1).canJoin((Domain) o2);
        }
        return false;
    }

    public static Domain join(Object o1, Object o2) {
        return (Domain) ((Domain) o1).join((Domain) o2);
    }

    public static Domain apply(Function f, Domain d) {
        return d.apply(f);
    }

    // TODO: figure out priority
    public static Domain apply(BiFunction f, Domain d1, Domain d2) {
        return d1.apply(f, d2);
    }

    public static boolean contains(Domain d, Object o) {
        return d.contains(o);
    }
}
