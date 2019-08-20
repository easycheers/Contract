using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using Helper = Neo.SmartContract.Framework.Helper;
using System;
using System.ComponentModel;
using System.Numerics;

namespace ECT
{
    public class ECT : SmartContract
    {
        [DisplayName("transfer")]
        public static event Action<byte[], byte[], BigInteger> Transferred;
        [DisplayName("burn")]
        public static event Action<byte[], BigInteger> Burned;
        [DisplayName("neobuyect")]
        public static event Action<byte[], byte[], BigInteger, BigInteger, BigInteger> Neobuyect;
        [DisplayName("one")]
        public static event Action<BigInteger, BigInteger, BigInteger, BigInteger> one;

        private static readonly byte[] AssetId = Helper.HexToBytes("9b7cffdaa674beae0f930ebe6085af9093e5fe56b34a5c220ccdcf6efc336fc5"); // littleEndian
        private static readonly byte[] Owner = "AJYrWm4ZrYvFMrnmervVyoztVy61nw5PVY".ToScriptHash(); //Owner Address
        //private static readonly BigInteger total_amount = (BigInteger)1460000000 * 100000000; // total token amount
        //private static readonly BigInteger first = (BigInteger)120000000 * 100000000; //Phase I Pre-sale
        //private static readonly BigInteger second = (BigInteger)660000000 * 100000000; //Phase II Pre-sale
        //private static readonly BigInteger third = (BigInteger)1060000000 * 100000000; //Phase III Pre-sale
        //private static readonly BigInteger fourth = (BigInteger)1460000000 * 100000000; //Phase IV Pre-sale

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

                if (method == "deploy") return Deploy((BigInteger)args[0]);

                if (method == "burn") return Burn((byte[])args[0], (BigInteger)args[1]);

                if (method == "close") return Close();

                if (method == "price") return Price();

                if (method == "setneoprice") return SetNeoPrice((BigInteger)args[0]);

                if (method == "setphase") return SetPhase((BigInteger)args[0]);

                if (method == "getphase") return GetPhase();

                if (method == "buy") return Buy(callscript);

                if (method == "upgrade")
                {
                    return Upgrade(args);
                }
            }
            else if (Runtime.Trigger == TriggerType.VerificationR) //Backward compatibility, refusing to accept other assets
            {
                var currentHash = ExecutionEngine.ExecutingScriptHash;
                var tx = ExecutionEngine.ScriptContainer as Transaction;
                foreach (var output in tx.GetOutputs())
                {
                    if (output.ScriptHash == currentHash && output.AssetId.AsBigInteger() != AssetId.AsBigInteger())
                        return false;
                }
                return true;
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
        public static byte Decimals() => 8;

        private static bool IsPayable(byte[] to)
        {
            var c = Blockchain.GetContract(to);
            return c == null || c.IsPayable;
        }

        [DisplayName("name")]
        public static string Name() => "Easy Cheers Token"; //name of the token

        [DisplayName("symbol")]
        public static string Symbol() => "ECT"; //symbol of the token

        [DisplayName("supportedStandards")]
        public static string[] SupportedStandards() => new string[] { "NEP-5", "NEP-7", "NEP-10" };

        [DisplayName("totalSupply")]
        public static BigInteger TotalSupply()
        {
            StorageMap contract = Storage.CurrentContext.CreateMap(nameof(contract));
            return contract.Get("Supply").AsBigInteger();
        }

        private static bool doTransfer(byte[] from, byte[] to, BigInteger amount)
        {
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

            return doTransfer(from, to, amount);
        }

        private static bool Deploy(BigInteger bp)
        {
            BigInteger total_amount = (BigInteger)1460000000 * 100000000; // total token amount
            if (!Runtime.CheckWitness(Owner))
                return false;

            StorageMap contract = Storage.CurrentContext.CreateMap(nameof(contract));
            byte[] total_supply = contract.Get("Supply");
            if (total_supply != null && total_supply.Length != 0) return false;
            contract.Put("Supply", total_amount);
            contract.Put("neoprice", bp);
            contract.Put("canBuy", 1);
            contract.Put("phase", 1);
            contract.Put("sell", 0);

            StorageMap asset = Storage.CurrentContext.CreateMap(nameof(asset));
            asset.Put(ExecutionEngine.ExecutingScriptHash, total_amount);
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

            StorageMap contract = Storage.CurrentContext.CreateMap(nameof(contract));
            BigInteger total_supply = contract.Get("Supply").AsBigInteger();
            contract.Put("Supply", total_supply - burnCount);

            Burned(from, burnCount);
            return true;
        }

        public static BigInteger Price()
        {
            StorageMap contract = Storage.CurrentContext.CreateMap(nameof(contract));
            BigInteger price = contract.Get("neoprice").AsBigInteger();
            BigInteger ph = contract.Get("phase").AsBigInteger();
            // 当rate的精度为8时
            if (ph == 1) price = price * 240 / 100000000;
            if (ph == 2) price = price * 120 / 100000000;
            if (ph == 3) price = price * 80 / 100000000;
            if (ph == 4) price = price * 60 / 100000000;

            return price;
        }

        private static bool SetNeoPrice(BigInteger bp)
        {
            // 这里需要限制发送Deploy指令的必须是管理员,怎么做呢?
            if (!Runtime.CheckWitness(Owner))
                return false;

            StorageMap contract = Storage.CurrentContext.CreateMap(nameof(contract));
            contract.Put("neoprice", bp);
            return true;
        }

        private static bool SetPhase(BigInteger ph)
        {
            // 这里需要限制发送Deploy指令的必须是管理员,怎么做呢?
            if (!Runtime.CheckWitness(Owner))
                return false;

            StorageMap contract = Storage.CurrentContext.CreateMap(nameof(contract));
            contract.Put("phase", ph);
            return true;
        }

        private static BigInteger GetPhase()
        {
            StorageMap contract = Storage.CurrentContext.CreateMap(nameof(contract));
            return contract.Get("phase").AsBigInteger();
        }

        private static bool Close()
        {
            // 这里需要限制发送Deploy指令的必须是管理员,怎么做呢?
            if (!Runtime.CheckWitness(Owner))
                return false;

            StorageMap contract = Storage.CurrentContext.CreateMap(nameof(contract));
            byte[] cb = contract.Get("canBuy");
            if (cb == null || cb.Length == 0) return false;
            if (cb.AsBigInteger() == 0) return true;

            contract.Put("canBuy", 0);
            return true;
        }

        private static bool CanBuy()
        {
            StorageMap contract = Storage.CurrentContext.CreateMap(nameof(contract));
            byte[] cb = contract.Get("canBuy");
            if (cb != null && cb.Length != 0 && cb.AsBigInteger() == 1) return true;

            return false;
        }

        // check whether asset is neo and get sender script hash
        private static byte[] GetSender()
        {
            Transaction tx = (Transaction)ExecutionEngine.ScriptContainer;
            TransactionOutput[] reference = tx.GetReferences();
            // you can choice refund or not refund
            foreach (TransactionOutput output in reference)
            {
                if (output.AssetId.AsBigInteger() == AssetId.AsBigInteger()) return output.ScriptHash;
            }
            return new byte[] { };
        }

        // get smart contract script hash
        private static byte[] GetReceiver()
        {
            return ExecutionEngine.ExecutingScriptHash;
        }

        // get all you contribute neo amount
        private static ulong GetContributeValue()
        {
            Transaction tx = (Transaction)ExecutionEngine.ScriptContainer;
            TransactionOutput[] outputs = tx.GetOutputs();
            ulong value = 0;
            // get the total amount of Gas
            // 获取转入智能合约地址的Gas总量
            foreach (TransactionOutput output in outputs)
            {
                if (output.ScriptHash == GetReceiver() && output.AssetId.AsBigInteger() == AssetId.AsBigInteger())
                {
                    value += (ulong)output.Value;
                }
            }
            return value;
        }

        //whether over contribute capacity, you can get the token amount
        private static BigInteger CanBuyTokenNum(BigInteger value)
        {
            BigInteger first = (BigInteger)120000000 * 100000000; //Phase I Pre-sale
            BigInteger second = (BigInteger)660000000 * 100000000; //Phase II Pre-sale
            BigInteger third = (BigInteger)1060000000 * 100000000; //Phase III Pre-sale
            BigInteger fourth = (BigInteger)1460000000 * 100000000; //Phase IV Pre-sale
            BigInteger remain = 0;

            StorageMap contract = Storage.CurrentContext.CreateMap(nameof(contract));
            BigInteger price = contract.Get("neoprice").AsBigInteger();
            BigInteger ph = contract.Get("phase").AsBigInteger();
            BigInteger sell = contract.Get("sell").AsBigInteger();
            // 当price的精度为8时
            if (ph == 1)
            {
                price = price * 240 / 100000000;
                remain = first - sell;
            }
            if (ph == 2)
            {
                price = price * 120 / 100000000;
                remain = second - sell;
            }
            if (ph == 3)
            {
                price = price * 80 / 100000000;
                remain = third - sell;
            }
            if (ph == 4)
            {
                price = price * 60 / 100000000;
                remain = fourth - sell;
            }
            BigInteger token = value * price;
            if (remain < token)
            {
                token = 0;
            }
            one(value, price, remain, sell);
            return token;
        }

        private static bool Buy(byte[] callscript)
        {
            if (!CanBuy()) return false;

            byte[] sender = GetSender();
            if (!Runtime.CheckWitness(sender))
                return false;
            if (sender.Length == 0) return false;
            ulong contribute_value = GetContributeValue();
            BigInteger tokNum = CanBuyTokenNum((BigInteger)contribute_value);
            if (tokNum == 0)
            {
                // 交易失败，NEO 无法退回
                throw new InvalidOperationException("cannot buy.");
            }
            // -----------使用nep5转账
            if (doTransfer(ExecutionEngine.ExecutingScriptHash, sender, tokNum) == false)
            {
                // 交易失败，NEO无法自动返还?
                throw new InvalidOperationException("nep5 transfer error.");
            }
            //----------
            StorageMap contract = Storage.CurrentContext.CreateMap(nameof(contract));
            BigInteger sell = contract.Get("sell").AsBigInteger();
            contract.Put("sell", sell + tokNum);

            Neobuyect(ExecutionEngine.ExecutingScriptHash, sender, contribute_value, tokNum, sell + tokNum);
            return true;
        }

        #region 升级合约,耗费490,仅限管理员
        private static bool Upgrade(object[] args)
        {
            //不是管理员 不能操作
            if (!Runtime.CheckWitness(Owner))
                return false;

            if (args.Length != 1 && args.Length != 9)
                return false;

            byte[] script = Blockchain.GetContract(ExecutionEngine.ExecutingScriptHash).Script;
            byte[] new_script = (byte[])args[0];
            //如果传入的脚本一样 不继续操作
            if (script == new_script)
                return false;

            byte[] parameter_list = new byte[] { 0x07, 0x10 };
            byte return_type = 0x05;
            ContractPropertyState need_storage = (ContractPropertyState)(object)07;
            string name = "ECT";
            string version = "1";
            string author = "james";
            string email = "0";
            string description = "Easy Cheers Token";

            if (args.Length == 9)
            {
                parameter_list = (byte[])args[1];
                return_type = (byte)args[2];
                need_storage = (ContractPropertyState)args[3];
                name = (string)args[4];
                version = (string)args[5];
                author = (string)args[6];
                email = (string)args[7];
                description = (string)args[8];
            }
            Contract.Migrate(new_script, parameter_list, return_type, need_storage, name, version, author, email, description);
            return true;
        }
        #endregion
    }
}