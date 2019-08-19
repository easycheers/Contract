using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;

namespace Joy
{
    public class Joy : SmartContract
    {
        [DisplayName("transfer")]
        public static event Action<byte[], byte[], BigInteger> Transferred;
        [DisplayName("burn")]
        public static event Action<byte[], BigInteger> Burned;

        private static readonly byte[] Owner = "ATDbX7x3Aq8TJszPt34QStJa7sz3z54m8q".ToScriptHash(); //Owner Address
        private static readonly BigInteger total_amount = (BigInteger)100000000; // total token amount

        public static object Main(string method, object[] args)
        {
            if (Runtime.Trigger == TriggerType.Verification)
            {
                return Runtime.CheckWitness(Owner);
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                var callscript = ExecutionEngine.CallingScriptHash;

                if (method == "balanceOf") return BalanceOf((byte[])args[0]);

                if (method == "decimals") return Decimals();

                if (method == "name") return Name();

                if (method == "symbol") return Symbol();

                if (method == "supportedStandards") return SupportedStandards();

                if (method == "totalSupply") return TotalSupply();

                if (method == "transfer") return Transfer((byte[])args[0], (byte[])args[1], (BigInteger)args[2], callscript);

                if (method == "deploy") return Deploy((BigInteger)args[0], callscript);

                if (method == "burn") return Burn((byte[])args[0], (BigInteger)args[1]);
            }
            return false;
        }

        [DisplayName("balanceOf")]
        public static BigInteger BalanceOf(byte[] account)
        {
            if (account.Length != 20)
                throw new InvalidOperationException("The parameter account SHOULD be 20-byte addresses.");
            StorageMap asset = Storage.CurrentContext.CreateMap(nameof(asset));
            return asset.Get(account).AsBigInteger();
        }
        [DisplayName("decimals")]
        public static byte Decimals() => 0;

        private static bool IsPayable(byte[] to)
        {
            var c = Blockchain.GetContract(to);
            return c == null || c.IsPayable;
        }

        [DisplayName("name")]
        public static string Name() => "Joy Company Stock"; //name of the token

        [DisplayName("symbol")]
        public static string Symbol() => "JOY"; //symbol of the token

        [DisplayName("supportedStandards")]
        public static string[] SupportedStandards() => new string[] { "NEP-5", "NEP-7", "NEP-10" };

        [DisplayName("totalSupply")]
        public static BigInteger TotalSupply()
        {
            StorageMap contract = Storage.CurrentContext.CreateMap(nameof(contract));
            return contract.Get("Supply").AsBigInteger();
        }

        //Methods of actual execution
        private static bool Transfer(byte[] from, byte[] to, BigInteger amount, byte[] callscript)
        {
            //Check parameters
            if (from.Length != 20 || to.Length != 20)
                throw new InvalidOperationException("The parameters from and to SHOULD be 20-byte addresses.");
            if (amount <= 0)
                throw new InvalidOperationException("The parameter amount MUST be greater than 0.");
            if (!IsPayable(to))
                return false;
            if (!Runtime.CheckWitness(from) && from.AsBigInteger() != callscript.AsBigInteger())  /*0.2*/
                return false;
            StorageMap asset = Storage.CurrentContext.CreateMap(nameof(asset));
            var fromAmount = asset.Get(from).AsBigInteger();  /*0.1*/
            if (fromAmount < amount)
                return false;
            if (from == to)
                return true;

            //Reduce payer balances
            if (fromAmount == amount)
                asset.Delete(from); /*0.1*/
            else
                asset.Put(from, fromAmount - amount); /*1*/

            //Increase the payee balance
            var toAmount = asset.Get(to).AsBigInteger();  /*0.1*/
            asset.Put(to, toAmount + amount);  /*1*/

            Transferred(from, to, amount);
            return true;
        }

        private static bool Deploy(BigInteger spy, byte[] callscript)
        {
            if (!Runtime.CheckWitness(Owner))
                return false;

            StorageMap contract = Storage.CurrentContext.CreateMap(nameof(contract));
            byte[] total_supply = contract.Get("Supply");
            if (total_supply != null && total_supply.Length != 0) return false;
            contract.Put("Supply", spy);

            StorageMap asset = Storage.CurrentContext.CreateMap(nameof(asset));
            asset.Put(Owner, spy);

            return true;
        }

        private static bool Burn(byte[] from, BigInteger burnCount)
        {
            if (!Runtime.CheckWitness(from))
                return false;

            if (from.Length != 20)
                throw new InvalidOperationException("The parameters from and to SHOULD be 20-byte addresses.");
            if (burnCount <= 0)
                throw new InvalidOperationException("The parameter amount MUST be greater than 0.");

            StorageMap asset = Storage.CurrentContext.CreateMap(nameof(asset));
            var fromAmount = asset.Get(from).AsBigInteger();  /*0.1*/
            if (fromAmount < burnCount)
                return false;

            //Reduce payer balances
            if (fromAmount == burnCount)
                asset.Delete(from); /*0.1*/
            else
                asset.Put(from, fromAmount - burnCount); /*1*/

            Burned(from, burnCount);
            return true;
        }

    }
}