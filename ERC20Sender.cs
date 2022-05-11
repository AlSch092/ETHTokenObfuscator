using System;
using System.Numerics;
using System.Threading.Tasks;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Web3.Accounts;
using Nethereum.Contracts;
using Nethereum.Web3;
using Nethereum.RPC.Eth.DTOs;
using static NethTest.DBManager;
using System.Threading;

namespace NethTest
{
    public class ERC20Sender
    {
        //This is the contract address of an already deployed smartcontract in the Mainnet
        private string ContractAddress { get; set; }
        private string newTxHash { get; set; }

        private string FromAddr { get; set; }
        private string FromPrivKey { get; set; }
        private string ToAddr { get; set; }
        private uint Amount { get; set; }

        private BigInteger Balance { get; set; }
        private uint Gas { get; set; }

        private uint Decimals { get; set; }

        private DBManager DBM { get; set; }

	private string URL { get; set; } //endpoint url

        public ERC20Sender(string From, string FromPvtKey, string To, uint Qty, uint Gas, uint Decimals)
        {
            this.FromAddr = From;
            this.FromPrivKey = FromPvtKey;
            this.ToAddr = To;
            this.Amount = Qty;
            this.Gas = Gas;
            this.Decimals = Decimals;
        }

        public ERC20Sender(string AccountPublicKey, string AccountPvtKey)
        {
            this.FromAddr = AccountPublicKey;
            this.FromPrivKey = AccountPvtKey;
            this.ContractAddress = "0xYOURCONTRACT";
            this.Decimals = 18;
        }

        public bool ForwardSubmission(ScramblerEntry SE)
        {
            Console.WriteLine("Inside Forwardsubmission..");

            if (SE.InitialFailed == true)
                return false;

            //check if initial TX was successful
            bool success = DidTransactionSucceed(SE.InitialTxHash);


            if (success)
            {
                //if successful, check the current balance, verify it's larger or equal to amount recv'd
                SE.InitialCompleted = true;

                this.ContractAddress = SE.ContractAddress;

                BalanceAsync().Wait();
                Console.ReadLine();

                //send out payment to other middle wallet or end user
                if (this.Balance >= SE.Amount)
                {
                    Console.WriteLine("Balance > amount, procee to send");

                    if (this.ContractAddress != null)
                    {
                        //for, x = 1, * 10 for each decimal...
                        uint x = 1;
                        for(uint y = 0; y < this.Decimals; y++)
                        {
                            x = x * 10;
                        }


                        TransferAsync("0xToAddr", new BigInteger(123)).Wait();
                        //TransferAsync(SE.ToAddr, SE.Amount).Wait();

                        //verify the end transaction succeeded, add some timer or failsafe to this doesnt get stuck
                        while (IsTransactionPending(this.newTxHash))
                        {
                            Console.WriteLine("waiting for new Tx to succeed: " + this.newTxHash);
                        }

                        //change db to impact new effects
                   
                        Console.WriteLine("Success! Check: " + this.newTxHash);
                        SE.nCompleted = true;
                        SE.EndingTxHash = this.newTxHash;
                        DBM.AlterRecord(SE);
                    }
                }
            }
            else
            {
                //update db to tell this was a fail initially


                SE.InitialCompleted = false;
                Console.WriteLine("Initial send failed from user, delete their entry from DB and notify them");
                Console.ReadLine();
                return false;
            }

            return true;
        }

        public void QueryForSubmissions()
        {
            DBM = new DBManager();
            bool isLooping = true;
            

            while (isLooping == true)
            {
                ScramblerEntry SE;

                try
                {
                    SE = DBM.GetOldestEntry();
                   
                    if (SE == null)
                    {
                        Console.WriteLine("No current inputs...");
                        return;
                    }
                    else
                    {

                        if (ForwardSubmission(SE))
                        {
                            Console.WriteLine("Forwarded submission: " + SE.EndingTxHash);
                        }
                        else
                        {
                            Console.WriteLine("Failed to redirect submission: " + SE.InitialTxHash);
                        }
                    }
                }
                catch
                {
                    Console.WriteLine("Failed to fetch SE or no current inputs, checking unsent entries...");


                    //search for entries "left over?"
                    ScramblerEntry S = DBM.GetUnsentEntry();

                    Console.WriteLine("Got unsent entry!");

                    if(S.InitialTxHash == null)
                    {
                        S.InitialFailed = true;
                        S.InitialCompleted = false;
                    }
                    else
                    {
                        if(DidTransactionSucceed(S.InitialTxHash))
                        {
                            S.InitialFailed = false;
                            S.InitialCompleted = true;
                            DBM.AlterRecord(S);
                        }
                    }
                }

               Thread.Sleep(3000);
            }
            Console.ReadLine();
        }

        public async Task GetEthBalance()
        { 

            var web3 = new Web3("https://mainnet.infura.io/v3/7a7a732056744fde8ba8bbfafc39b44d");
            var balance = await web3.Eth.GetBalance.SendRequestAsync("0xSomeAccount");
        }


        public async Task BalanceAsync()
        {
            //Replace with your own
            var senderAddress = this.FromAddr;

            // Note: in this sample, a special INFURA API key is used
            var url = "https://mainnet.infura.io/v3/yourAPI";

            //no private key we are not signing anything (read only mode)
            var web3 = new Web3(url);

            var balanceOfFunctionMessage = new BalanceOfFunction()
            {
                Owner = senderAddress,
            };

            var balanceHandler = web3.Eth.GetContractQueryHandler<BalanceOfFunction>();
            var balance = await balanceHandler.QueryAsync<BigInteger>(this.ContractAddress, balanceOfFunctionMessage);
     
            Console.WriteLine("Balance of token: " + balance);
            this.Balance = balance;
        }

        public async Task TransferAsync(string to, BigInteger amount)
        {
            //Replace with your own
            var senderAddress = this.FromAddr;
            var privatekey = this.FromPrivKey;

            BigInteger newAmount = amount * (10 ^ this.Decimals); //todo check this

            var web3 = new Web3(new Account(privatekey), URL); //creates valid/sign account

            var transactionMessage = new TransferFunction()
            {
           
                FromAddress = senderAddress,
                To = to,
                TokenAmount = new BigInteger(123),
                Gas = 100000

            };

            Console.WriteLine("FromAddr: " + transactionMessage.FromAddress + " To: " + transactionMessage.To + " Amount: " + transactionMessage.TokenAmount + "  Gas: " + transactionMessage.Gas + " " + transactionMessage.GasPrice);

            AccountTransactionSigningInterceptor a = new AccountTransactionSigningInterceptor(privatekey, web3.Client);
            
            var transferHandler = web3.Eth.GetContractTransactionHandler<TransferFunction>();


            var transactionHash = await transferHandler.SendRequestAsync(ContractAddress, transactionMessage);
            Console.WriteLine("Transfer txHash: " + transactionHash);
            this.newTxHash = Convert.ToString(transactionHash);           
        }


        public bool DidTransactionSucceed(string txid)
        {
            Console.WriteLine("Checking for Tx success: " + txid);

            // Note: in this sample, a special INFURA API key is used
            var url = "https://mainnet.infura.io/v3/7a7a732056744fde8ba8bbfafc39b44d";

            var web3 = new Web3(new Account(this.FromPrivKey), url); //creates valid/sign account

            var rcpt = web3.TransactionManager.TransactionReceiptService.PollForReceiptAsync(txid).Result;

            Console.WriteLine("Success: " + rcpt.Succeeded());

            return rcpt.Succeeded();
        }


        public bool IsTransactionPending(string txid)
        {
            // Note: in this sample, a special INFURA API key is used
            var url = "https://mainnet.infura.io/v3/yourAPI";

            var web3 = new Web3(new Account(this.FromPrivKey), url); //creates valid/sign account

            var rcpt = web3.TransactionManager.TransactionReceiptService.PollForReceiptAsync(txid).Result;

            Console.WriteLine("Pending: " + rcpt.BlockNumber);

            if (rcpt.BlockNumber == null)
                return true;

            else
                return false;
        }

        internal async Task<ulong> GetTransactionAmount(string txid)
        {
            // Note: in this sample, a special INFURA API key is used
            var url = "https://mainnet.infura.io/v3/yourAPI";

            var web3 = new Web3(new Account(this.FromPrivKey), url); //creates valid/sign account
            var result = await web3.Eth.Transactions.GetTransactionByHash.SendRequestAsync(txid);

            string newInput = result.Input.Remove(0, 2); //remove '0x'

            string strVal = newInput.Substring(104, 32);
            ulong amountSent = 0;

            try
            {
                amountSent = Convert.ToUInt64(strVal, 16);

            }
            catch
            {
                Console.WriteLine("Could not convert sent amount in txid: " + txid);
            }

            Console.WriteLine("Amount sent: " + amountSent);

            var rcpt = web3.TransactionManager.TransactionReceiptService.PollForReceiptAsync(txid).Result;

            Console.WriteLine("Success: " + rcpt.Succeeded());

            return amountSent;
        }

        /// <summary>
        /// Attempt to get the raw hex directly from the geth node
        /// </summary>
        /// <param name="txid"></param>
        /// <returns></returns>
        internal async Task<string> GetTransaction(string txid)
        {
            // Note: in this sample, a special INFURA API key is used
            var url = "https://mainnet.infura.io/v3/yourAPI";

            var web3 = new Web3(new Account(this.FromPrivKey), url); //creates valid/sign account
            var result = await web3.Eth.Transactions.GetTransactionByHash.SendRequestAsync(txid);
           
            
            string newInput = result.Input.Remove(0, 2); //remove '0x'

            string strVal = newInput.Substring(104, 32);
            ulong amountSent = 0;

            try
            {
                amountSent = Convert.ToUInt64(strVal, 16);
            }
            catch
            {
                Console.WriteLine("Could not convert sent amount in txid: " + txid);
            }

            Console.WriteLine("From: " + result.From + "  " + "To: " + result.To + " Gas: " + result.Gas + " Amount sent: " + amountSent);

            var rcpt = web3.TransactionManager.TransactionReceiptService.PollForReceiptAsync(txid).Result;

            Console.WriteLine("Success: " + rcpt.Succeeded());

            return result.Input;
        }

        [Function("transfer", "bool")]
        public class TransferFunction : FunctionMessage
        {
            [Parameter("address", "_to", 1)]
            public string To { get; set; }

            [Parameter("uint256", "_value", 2)]
            public BigInteger TokenAmount { get; set; }
        }

        [Function("balanceOf", "uint256")]
        public class BalanceOfFunction : FunctionMessage
        {

            [Parameter("address", "_owner", 1)]
            public string Owner { get; set; }

        }
    }
}
