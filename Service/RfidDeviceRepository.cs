﻿using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using RfidSPA.Data;
using RfidSPA.Models.Entities;
using RfidSPA.Service.Interfaces;
using RfidSPA.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RfidSPA.Service
{
    public class RfidDeviceRepository : IRfidDeviceRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAcessor;
        private readonly string _appCurrentUserID;


        public RfidDeviceRepository(ApplicationDbContext context, IHttpContextAccessor httpContextAcessor)
        {
            _context = context;
            _httpContextAcessor = httpContextAcessor;
            _appCurrentUserID = _httpContextAcessor.HttpContext.User.Claims.Single(c => c.Type == "id").Value;
        }



        public IEnumerable<RfidDevice> GetAllRfids()
        {

            return _context.RfidDevice.ToList();
        }


        public RfidDevice RfidByCode(string code)
        {
            return _context.RfidDevice.Where(i => i.RfidDeviceCode == code && i.ApplicationUserID == _appCurrentUserID).SingleOrDefault();
        }

        public RfidDevice RfidByID(long id)
        {
            return _context.RfidDevice.Where(i => i.RfidDeviceID == id).SingleOrDefault();
        }

        public List<RfidDevice> RfidsByAnagraficaID(long ID)
        {
            return _context.RfidDevice.Where(i => i.AnagraficaID == ID && i.ApplicationUserID == _appCurrentUserID).ToList();
        }

        public bool CeateNewRfid(AnagraficaRfidDeviceModel  item)
        {

            try
            {
                var l_anag = _context.Anagrafica.Where(i => i.Email == item.anagrafica.Email && i.ApplicationUserID == _appCurrentUserID).SingleOrDefault();

                // check id user exists 
                // se user non esiste crea uno nuovo 
                if (l_anag == null)
                {
                    Anagrafica anag = item.anagrafica;
                    anag.CreationDate = DateTime.Now;
                    anag.ApplicationUserID =  _appCurrentUserID;
                    _context.Anagrafica.Add(anag);
                    _context.SaveChanges();

                    l_anag = _context.Anagrafica.Where(i => i.Email == item.anagrafica.Email && i.ApplicationUserID == _appCurrentUserID).SingleOrDefault();

                }

                item.anagrafica.AnagraficaID = l_anag.AnagraficaID;
               
                var rfid = _context.RfidDevice
                    .Where(i => i.RfidDeviceCode == item.device.RfidDeviceCode && i.ApplicationUserID == _appCurrentUserID)
                    .SingleOrDefault();


                if (rfid != null)
                {
                    rfid.AnagraficaID = item.anagrafica.AnagraficaID;
                    rfid.ExpirationDate = item.device.ExpirationDate;
                    rfid.LastModifiedDate = DateTime.Now;
                    rfid.Credit = 0;
                    rfid.Active = true;

                    //update
                    _context.RfidDevice.Update(rfid);
                    _context.SaveChanges();

                    //aggiorna history
                    updateRfidHistory(rfid, RfidOperations.Assegna);
                }
                else
                {
                    // new 

                    RfidDevice l_rfid = new RfidDevice();
                    l_rfid.RfidDeviceCode = item.device.RfidDeviceCode;
                    l_rfid.ExpirationDate = item.device.ExpirationDate;
                    l_rfid.CreationDate = DateTime.Now;
                    l_rfid.LastModifiedDate = DateTime.Now;
                    l_rfid.Credit = 0;
                    l_rfid.ApplicationUserID = item.device.ApplicationUserID;
                    l_rfid.Active = true;
                    l_rfid.AnagraficaID = l_anag.AnagraficaID;

                    _context.RfidDevice.AddAsync(l_rfid);
                    _context.SaveChanges();

                    //aggiorna history
                    updateRfidHistory(l_rfid, RfidOperations.Assegna);
                }

                return true;

            }
            catch (Exception e)
            {
                return false;
            }
        }


        // paga con il dispositivo
        public bool PaidByRfid(PaidModel paidModel)
        {

            bool result = true;
            try
            {
                var rfid = RfidByCode(paidModel.RfidCode);
                if (rfid != null)
                {
                    if(rfid.AnagraficaID == null || rfid.AnagraficaID ==0)
                    {
                        return false; // non è associata nessuna anagrafica 
                    }
                    RfidDeviceTransaction trans = new RfidDeviceTransaction();
                    trans.RfidDeviceCode = rfid.RfidDeviceCode;
                    trans.AnagraficaID = rfid.AnagraficaID;
                    trans.ApplicationUserID = rfid.ApplicationUserID;
                    trans.TransactionOperation = (int)TransactionOperation.Pagamento;
                    trans.TransactionDate = DateTime.Now;
                    trans.Importo = paidModel.Price;
                    trans.Descrizione = paidModel.Descrizione;
                    trans.PaydOff = false;

                    rfid.Credit += paidModel.Price;
                    _context.Update(rfid);
                    _context.RfidDeviceTransaction.Add(trans);
                    _context.SaveChanges();
                }
                else result = false;
            }

            catch (Exception e)
            {
                result = false;
            }

            return result;

        }



        // salda il conto e restituisci il dissassocia il dispositivo da l'utente 
        public bool paidOffRfid(string code)
        {

            try
            {
                var listTr = _context.RfidDeviceTransaction
                .Where(i => i.PaydOff == false
                        && i.RfidDeviceCode == code
                        && i.AnagraficaID != null
                        && i.ApplicationUserID == _appCurrentUserID
                        && i.TransactionOperation == (int)TransactionOperation.Pagamento)
                 .ToList();
                var rfid = _context.RfidDevice
                    .Where(i => i.RfidDeviceCode == code
                       && i.AnagraficaID != null
                       && i.ApplicationUserID == _appCurrentUserID)
                    .SingleOrDefault();
            
           

                if (listTr != null)
                {
                    foreach (var item in listTr)
                    {
                        item.PaydOff = true;
                        item.PaydOffDate = DateTime.Now;
                        _context.RfidDeviceTransaction.Update(item);

                    }
                }
                if (rfid != null){


                    _context.RfidDeviceTransaction.Add(new RfidDeviceTransaction
                    {
                        AnagraficaID = rfid.AnagraficaID,
                        ApplicationUserID = rfid.ApplicationUserID,
                        Descrizione = "Saldo conto",
                        Importo = rfid.Credit,
                        RfidDeviceCode = rfid.RfidDeviceCode,
                        TransactionOperation = (int)TransactionOperation.SaldoDebito,
                        PaydOff = true,
                        PaydOffDate = DateTime.Now,
                        TransactionDate = DateTime.Now
                    });
                    rfid.Credit = 0;
                    rfid.AnagraficaID = null;
                    _context.Update(rfid);
                    _context.SaveChanges();
                    updateRfidHistory(rfid, RfidOperations.Restituisci);


                    return true;
                }
                else return false;

            }
           
             catch
            {
                return false;
            }

        }

        public bool paidOffAllRfids(List<RfidDevice> listRfids)
        {

            try
            {
                foreach (var rfid in listRfids)
                {

                    var res = paidOffRfid(rfid.RfidDeviceCode);
                    if (res == false) return false;
                }

                return true;
            }
            catch (Exception e)
            {
                return false;
            }

        }

        public List<RfidDeviceTransaction> getAllTransactionsToPaydOff(string code)
        {

            List<RfidDeviceTransaction> listTr = new List<RfidDeviceTransaction>();

            listTr = _context.RfidDeviceTransaction
                .Where(i => i.PaydOff == false
                        && i.RfidDeviceCode == code
                        && i.AnagraficaID != null
                        && i.ApplicationUserID == _appCurrentUserID
                        && i.TransactionOperation == (int)TransactionOperation.Pagamento)
                 .ToList();

            return listTr;
        }

        public UserDetailViewModel getGeatailUserByEmail(string email)
        {
            var user = _context.Anagrafica.Where(i => i.Email == email).SingleOrDefault();

            if (user == null) return null;

            return new UserDetailViewModel { Anagrafica = user, Dispositivi = getDeviceByUser(user.AnagraficaID, true) };

        }

        public UserDetailViewModel getGeatailUserByRfidCode(string code)
        {
            var disp = _context.RfidDevice
                .Where(i => i.RfidDeviceCode == code
                    && i.Active == true
                    && i.AnagraficaID != null
                    && i.ApplicationUserID == _appCurrentUserID)
                .SingleOrDefault();

            if (disp == null) return null;
            var user = _context.Anagrafica.Where(i => i.AnagraficaID == disp.AnagraficaID).SingleOrDefault();

            if (user == null) return null;
            return new UserDetailViewModel { Anagrafica = user, Dispositivi = getDeviceByUser(user.AnagraficaID, true) };
        }

        #region localMethos 

        void updateRfidHistory(RfidDevice rfid, RfidOperations operation)
        {

            RfidDeviceHistory rfidHistory = new RfidDeviceHistory();
           
            rfidHistory.RfidDeviceCode = rfid.RfidDeviceCode;
            rfidHistory.InsertDate = DateTime.Now;
            rfidHistory.RfidDeviceOperation = (int)operation;
            rfidHistory.ApplicationUserID = rfid.ApplicationUserID;
            rfidHistory.Active = rfid.Active;
            rfidHistory.AnagraficaID = rfid.AnagraficaID;

            _context.RfidDeviceHistory.AddAsync(rfidHistory);
            _context.SaveChanges();


        }

        List<RfidDevice> getDeviceByUser(long id, bool? active = true)
        {
            return _context.RfidDevice.Where(i => i.AnagraficaID == id && i.Active == active).ToList();
        }

        public async  Task<List<RfidDevice>> getDevicesByApplicationUsers()
        {

            var result = await _context.RfidDevice.Where(i => i.ApplicationUserID == _appCurrentUserID).ToListAsync();
            return result;
        }







        #endregion

    }
}
