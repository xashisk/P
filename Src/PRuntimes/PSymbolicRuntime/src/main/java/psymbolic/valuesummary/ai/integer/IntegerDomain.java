package psymbolic.valuesummary.ai.integer;

import psymbolic.valuesummary.ai.bool.BooleanDomain;

public interface IntegerDomain {

    public IntegerDomain sum (IntegerDomain other);
    public IntegerDomain subtract (IntegerDomain other);
    public IntegerDomain multiply (IntegerDomain other);
    public IntegerDomain divide (IntegerDomain other);
    public IntegerDomain compareTo (IntegerDomain other);
    public BooleanDomain equals (IntegerDomain other);
    public BooleanDomain lt (IntegerDomain other);
    public BooleanDomain le (IntegerDomain other);
    public BooleanDomain gt (IntegerDomain other);
    public BooleanDomain ge (IntegerDomain other);

}
