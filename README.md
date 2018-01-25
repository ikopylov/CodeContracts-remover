# CodeContracts Remover
This repository contains Code Fixes for Roslyn that help to remove CodeContracts from source code.

List of provided analyzers and code fixes (in the order of application):
- __CR01_RetrieveCodeContractFromBase__ - indicates that the ```Contract.Requires``` method can be retrieved from Contract class of from base class;
- __CR02_RequiresGenericToIfThrow__ - indicates that ```Contract.Requires<TException>()``` method should be replaced with _if...throw_ statement;
- __CR03_ContractToDebugAssertReplace__ - indicates that ```Contract.Requires()```, ```Contract.Assert()``` and ```Contract.Assume()``` should be replaced with ```Debug.Assert()```;
- __CR04_EliminateCallsToContractMethods__ - indicates that all other ```Contract``` methods (Ensure, EnsureOnThrow, etc.) should be removed from source code;
- __CR05_EliminateContractClass__ - indicates that Contract class (class that marked with ```ContractClassForAttribute```) can be removed from source code;
- __CR06_EliminateInvariantMethods__ - indicates that Contract invariant method (method marked with ```ContractInvariantMethodAttribute```) can be removed from source code.


## Examples

1. __CR01_RetrieveCodeContractFromBase__
```C#
public class TestImpl : TestAbstract
{
    public override void Method1(string val)
    {
        throw new NotImplementedException();
    }
}

[ContractClass(typeof(TestAbstractCC))]
public abstract class TestAbstract
{
    public abstract void Method1(string val);
}
[ContractClassFor(typeof(TestAbstract))]
abstract class TestAbstractCC : TestAbstract
{
    public override void Method1(string val)
    {
        Contract.Requires(val != null);
        throw new NotImplementedException();
    }
}
```

will be translated to

```C#
public class TestImpl : TestAbstract
{
    public override void Method1(string val)
    {
        Contract.Requires(val != null);      // This line was extracted from TestAbstractCC
        throw new NotImplementedException();
    }
}

[ContractClass(typeof(TestAbstractCC))]
public abstract class TestAbstract
{
    public abstract void Method1(string val);
}
[ContractClassFor(typeof(TestAbstract))]
abstract class TestAbstractCC : TestAbstract
{
    public override void Method1(string val)
    {
        Contract.Requires(val != null);
        throw new NotImplementedException();
    }
}
```

2. __CR02_RequiresGenericToIfThrow__
```C#
static void Method(string val, int data)
{
    Contract.Requires<ArgumentNullException>(val != null);
    Contract.Requires<ArgumentOutOfRangeException>(data >= 0);
}
```

will be translated to
```C#
static void Method(string val, int data)
{
    if (val == null)
        throw new ArgumentNullException(nameof(val));
    if (data < 0)
        throw new ArgumentOutOfRangeException(nameof(data), "data >= 0");
}
```

3. __CR03_ContractToDebugAssertReplace__
```C#
static void Method(string val, int data)
{
    Contract.Requires(val != null);
    Contract.Assert(data >= 0);
}
```

will be translated to
```C#
static void Method(string val, int data)
{
    Debug.Assert(val != null, "val != null");
    Debug.Assert(data >= 0, "data >= 0");
}
```

4. __CR04_EliminateCallsToContractMethods__
```C#
static int Method(string val, int data)
{
    Contract.Ensures(Contract.Result<int>() > 0);
}
```

will be translated to
```C#
static int Method(string val, int data)
{
}
```

5. __CR05_EliminateContractClass__
```C#
public class TestImpl : TestAbstract
{
    public override void Method1(string val)
    {
        Contract.Requires(val != null);
        throw new NotImplementedException();
    }
}

[ContractClass(typeof(TestAbstractCC))]
public abstract class TestAbstract
{
    public abstract void Method1(string val);
}
[ContractClassFor(typeof(TestAbstract))]
abstract class TestAbstractCC : TestAbstract
{
    public override void Method1(string val)
    {
        Contract.Requires(val != null);
        throw new NotImplementedException();
    }
}
```

will be translated to

```C#
public class TestImpl : TestAbstract
{
    public override void Method1(string val)
    {
        Contract.Requires(val != null);
        throw new NotImplementedException();
    }
}

public abstract class TestAbstract
{
    public abstract void Method1(string val);
}
```


6. __CR06_EliminateInvariantMethods__
```C#
public class TestImpl : TestAbstract
{
    public string Data { get; set; }
    
    [ContractInvariantMethod]
    private void Invariant()
    {
        Contract.Invariant(Data != null);
    }	
}
```

will be translated to

```C#
public class TestImpl : TestAbstract
{
    public string Data { get; set; }
}
```
