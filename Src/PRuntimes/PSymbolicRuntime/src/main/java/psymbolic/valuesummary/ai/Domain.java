package psymbolic.valuesummary.ai;

import java.util.*;
import java.util.function.Function;
import java.util.function.BiFunction;

public interface Domain<T> {

    boolean canJoin(Domain<T> d);
    Domain<T> join(Domain<T> d);
    <U> Domain<U> apply(Function<T, U> f);
    <U, R> Domain<R> apply(BiFunction<T, U, R> f, Domain<U> other);
    boolean contains(T val);
    Domain<Boolean> domainEquals(Domain<T> other);
    Set<T> concretize();

}
