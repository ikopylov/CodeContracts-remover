using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Nmasa
{
    class Program
    {
        static void Main(string[] args)
        {
        }

        static void Test(string val)
        {
            if (val == null)
                throw new ArgumentNullException("val");
        }


        static void ContractTest(string val, int data)
        {
            Contract.Requires<ArgumentNullException>(val != null, "aaa" + "ab");
            Contract.Requires<ArgumentOutOfRangeException>(data >= 0);
            Contract.Requires<ArgumentException>(data != 0 && val != "fff");
            Contract.Requires<InvalidOperationException>(data < 100);
            Contract.Requires(val != null);
            System.Diagnostics.Contracts.Contract.Requires(val != null);
            Contract.Assert(val != null, "Message " + 1.ToString());
            System.Diagnostics.Debug.Assert(val != null);
        }
    }


    public class TestImpl : TestAbstract, ITestInterface, ITestInterfaceAbstract<int>
    {
        public override void Method1(string val)
        {
            throw new NotImplementedException();
        }

        public override void Method2(string val)
        {
            base.Method2(val);
        }

        public void Method3(string val)
        {
            throw new NotImplementedException();
        }

        public override void Method6<ZE>(string val)
        {
            throw new NotImplementedException();
        }

        public void Method7(string val)
        {
            throw new NotImplementedException();
        }
    }
    public class TestImplDeeper : TestImpl
    {
        public override void Method1(string val)
        {
            base.Method1(val);
        }
        public override void Method2(string val)
        {
            base.Method2(val);
        }
    }


    public class TestImpl2<TP> : TestAbstractGeneric<TP>, ITestInterfaceAbstract<TP>
    {
        public override void Method4(string val)
        {
            throw new NotImplementedException();
        }

        public override void Method5<M>(string val, M data)
        {
            throw new NotImplementedException();
        }

        public void Method7(string val)
        {
            throw new NotImplementedException();
        }
    }
    public class TestImpl3 : TestAbstractGeneric<int>
    {
        public override void Method4(string val)
        {
            throw new NotImplementedException();
        }

        public override void Method5<M>(string val, M data)
        {
            throw new NotImplementedException();
        }
    }


    [ContractClass(typeof(TestAbstractCC))]
    public abstract class TestAbstract
    {
        public abstract void Method1(string val);
        public virtual void Method2(string val)
        {
            Contract.Requires<ArgumentNullException>(val != null);

            return;
        }
        public abstract void Method6<Z>(string val);
    }
    [ContractClass(typeof(ITestInterfaceCC))]
    public interface ITestInterface
    {
        void Method3(string val);
    }
    [ContractClass(typeof(TestAbstractGenericCC<>))]
    public abstract class TestAbstractGeneric<T>
    {
        public abstract void Method4(string val);
        public abstract void Method5<Z>(string val, Z data);
    }
    [ContractClass(typeof(ITestInterfaceAbstractCC<>))]
    public interface ITestInterfaceAbstract<TX>
    {
        void Method7(string val);
    }


    [ContractClassFor(typeof(TestAbstract))]
    abstract class TestAbstractCC : TestAbstract
    {
        public override void Method1(string val)
        {
            Contract.Requires<ArgumentNullException>(val != null);
            throw new NotImplementedException();
        }
        public override void Method6<ZM>(string val)
        {
            Contract.Requires<ArgumentNullException>(val != null);
            throw new NotImplementedException();
        }
    }
    [ContractClassFor(typeof(ITestInterface))]
    abstract class ITestInterfaceCC : ITestInterface
    {
        public void Method3(string val)
        {
            Contract.Requires<ArgumentNullException>(val != null);
            throw new NotImplementedException();
        }
    }

    [ContractClassFor(typeof(TestAbstractGeneric<>))]
    abstract class TestAbstractGenericCC<TX> : TestAbstractGeneric<TX>
    {
        public override void Method4(string val)
        {
            Contract.Requires<ArgumentNullException>(val != null);
            throw new NotImplementedException();
        }
        public override void Method5<Z>(string val, Z data)
        {
            Contract.Requires<ArgumentNullException>(val != null);
            throw new NotImplementedException();
        }
    }

    [ContractClassFor(typeof(ITestInterfaceAbstract<>))]
    abstract class ITestInterfaceAbstractCC<AAAA> : ITestInterfaceAbstract<AAAA>
    {
        public void Method7(string val)
        {
            Contract.Requires<ArgumentException>(val != null);
            throw new NotImplementedException();
        }
    }
}
