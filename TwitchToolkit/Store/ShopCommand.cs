﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace TwitchToolkit.Store
{
    public class ShopCommand
    {
        public string message;
        public Viewer viewer;
        public IncItem product;
        public int calculatedprice = 0;
        public string errormessage = null;
        public string successmessage = null;
        private string item = null;
        private int quantity = 0;
        private string craftedmessage;
        public Item itemtobuy = null;

        public ShopCommand(string message, Viewer viewer)
        {
            string[] command = message.Split(' ');
            string productabr = command[1];
            this.message = message;
            this.viewer = viewer;

            Helper.Log($"debug {productabr.ToLower()}");
            this.product = Products.GetProduct(productabr.ToLower());

            if (this.product == null)
            {
                Helper.Log("Product is null");
                return;
            }

            Helper.Log("Configuring purchase");
            if (product.type == 0)
            { //event
                Helper.Log("Calculating price for event");
                if (this.product.amount < 0)
                {
                    return;
                }

                this.calculatedprice = this.product.amount;
                string[] chatmessage = command;
                craftedmessage = $"{this.viewer.username}: ";
                for (int i = 2; i < chatmessage.Length; i++)
                {
                    craftedmessage += chatmessage[i] + " ";
                }
                this.product.evt.chatmessage = craftedmessage;
            }
            else if (product.type == 1)
            { //item

                Settings.LoadItemsIfNotLoaded();

                try
                {
                    Helper.Log("Trying ItemEvent Checks");

                    Item itemtobuy = Item.GetItemFromAbr(command[0]);

                    if (itemtobuy == null)
                    {
                        return;
                    }

                    if (itemtobuy.price < 0)
                    {
                        return;
                    }

                    int itemPrice = itemtobuy.price;
                    int messageStartsAt = 3;

                    // check if 2nd index of command is a number
                    if (command.Count() > 2 && int.TryParse(command[2], out this.quantity))
                    { 
                        messageStartsAt = 3;
                    }
                    else if (command.Count() == 2)
                    {
                        // short command
                        this.quantity = 1;
                        messageStartsAt = 2;
                    }
                    else if (command[2] == "*")
                    {
                        Helper.Log("Getting max");
                        this.quantity = this.viewer.GetViewerCoins() / itemtobuy.price;
                        messageStartsAt = 3;
                        Helper.Log(this.quantity);
                    }
                    else
                    {
                        this.quantity = 1;
                        messageStartsAt = 2;
                        Helper.Log("Quantity not calculated");
                    }

                    string[] chatmessage = command;
                    craftedmessage = $"{this.viewer.username}: ";
                    for (int i = messageStartsAt; i < chatmessage.Length; i++)
                    {
                        craftedmessage += chatmessage[i] + " ";
                    }

                    try
                    {
                        if (itemPrice > 0)
                        {
                            Helper.Log($"item: {this.item} - price: {itemPrice} - quantity{this.quantity}");
                            this.calculatedprice = checked(itemtobuy.CalculatePrice(this.quantity));
                            this.itemtobuy = itemtobuy;
                        }
                    }
                    catch (OverflowException e)
                    {
                        Helper.Log("overflow in calculated price " + e.Message);
                    }


                }
                catch (InvalidCastException e)
                {
                    Helper.Log("Invalid product or quantity - " + e.Message);
                }
            }

            int MaxEventsTypeToCheck = Settings.MaxNeutralEventsPerInterval;
            if (this.product.karmatype == KarmaType.Good)
            {
                MaxEventsTypeToCheck = Settings.MaxGoodEventsPerInterval;
            }
            else if (this.product.karmatype == KarmaType.Neutral)
            {
                MaxEventsTypeToCheck = Settings.MaxNeutralEventsPerInterval;
            }
            else if (this.product.karmatype == KarmaType.Doom || this.product.karmatype == KarmaType.Bad)
            {
                MaxEventsTypeToCheck = Settings.MaxBadEventsPerInterval;
            }

            Helper.Log($"count {PurchaseLogger.CountRecentEventsOfType(this.product.karmatype, Settings.MaxEventsPeriod)} > max type {MaxEventsTypeToCheck - 1} ");

            if (this.calculatedprice <= 0)
            {
                // invalid price
                Helper.Log("Invalid price detected?");
            }
            else if (viewer.GetViewerCoins() < this.calculatedprice && !Settings.UnlimitedCoins)
            {
                // send message not enough coins
                this.errormessage = Helper.ReplacePlaceholder("TwitchToolkitNotEnoughCoins".Translate(), viewer: viewer.username, amount: this.calculatedprice.ToString(), first: viewer.GetViewerCoins().ToString());
            }
            else if (calculatedprice < Settings.MinimumPurchasePrice)
            {
                // does not meet minimum purchase price
                this.errormessage = Helper.ReplacePlaceholder("TwitchToolkitMinPurchaseNotMet".Translate(), viewer: this.viewer.username, amount: this.calculatedprice.ToString(), first: Settings.MinimumPurchasePrice.ToString());
            }
            else if (this.product.type == 0 && !this.product.evt.IsPossible())
            {
                 this.errormessage = $"@{this.viewer.username} " + "TwitchToolkitEventNotPossible".Translate();
            }
            else if (this.product.maxEvents < 1 && Settings.EventsHaveCooldowns)
            {
                this.errormessage = $"@{this.viewer.username} " + "TwitchToolkitEventOnCooldown".Translate();
            }
            else if (Settings.MaxEvents && (PurchaseLogger.CountRecentEventsOfType(this.product.karmatype, Settings.MaxEventsPeriod) > MaxEventsTypeToCheck - 1))
            {
                this.errormessage = $"@{this.viewer.username} " + "TwitchToolkitMaxEvents".Translate();
            }
            else
            {
                this.ExecuteCommand();
            }
        }

        private void ExecuteCommand()
        {
            // take user coins
            if (!Settings.UnlimitedCoins)
            {
                this.viewer.TakeViewerCoins(this.calculatedprice);
            }
            
            // create success message
            if (this.product.type == 0)
            {
                if (this.product.evt.IsPossible())
                { 
                    
                    // normal event
                    this.successmessage = Helper.ReplacePlaceholder("TwitchToolkitEventPurchaseConfirm".Translate(), first: this.product.name, viewer: this.viewer.username);
                    this.viewer.SetViewerKarma(Karma.CalculateNewKarma(this.viewer.GetViewerKarma(), this.product.karmatype, this.calculatedprice));

                    if (Settings.EventsHaveCooldowns)
                    {       
                        // take of a cooldown for event and schedule for it to be taken off
                        this.product.maxEvents--;
                        Settings.JobManager.AddNewJob(new ScheduledJob(Settings.EventCooldownInterval, new Func<object, bool>(IncrementProduct), product));
                    }
                }
                else
                {
                    // refund if event not possible anymore
                    this.viewer.GiveViewerCoins(this.calculatedprice);
                    return;
                }
            }
            else if (this.product.type == 1)
            {
                // care package 
                try
                {
                    this.product.evt = new Event(80, EventType.Good, EventCategory.Drop, 3, "Gold", () => true, (quote) => this.itemtobuy.PutItemInCargoPod(quote, this.quantity, this.viewer.username));
                }
                catch (InvalidCastException e)
                {
                    Helper.Log("Carepackage error " + e.Message);
                }

                if (this.product.evt == null)
                {
                    Helper.Log("Could not generate care package");
                    return;
                }

                this.successmessage = Helper.ReplacePlaceholder("TwitchToolkitItemPurchaseConfirm".Translate(), amount: this.quantity.ToString(), item: this.itemtobuy.abr, viewer: this.viewer.username);
                this.product.evt.chatmessage = craftedmessage;
                this.viewer.SetViewerKarma(Karma.CalculateNewKarma(this.viewer.GetViewerKarma(), this.product.karmatype, this.calculatedprice));

            }

            PurchaseLogger.LogPurchase(new Purchase(this.viewer.username, this.product.name, this.product.karmatype, this.calculatedprice, this.successmessage, DateTime.Now));
            // create purchase event
            Ticker.Events.Enqueue(this.product.evt);
        }

        public bool IncrementProduct(object obj)
        {
            IncItem product = obj as IncItem;
            product.maxEvents++;
            return true;
        }
    }
}