using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuikSharp.DataStructures;

namespace TrailingStop
{
    class Strategy
    {
        /// <summary>
        /// Наименование стратегии
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Признак активности
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// Номер стоп-заявки
        /// </summary>
        public long StopOrderNum { get; set; }

        /// <summary>
        /// Цена позиции
        /// </summary>
        public decimal PricePosition { get; set; }

        /// <summary>
        /// Операция стоп-заявки
        /// </summary>
        public Operation Operation { get; set; }

        /// <summary>
        /// Условие срабатывания стоп-заявки
        /// </summary>
        public Condition Condition { get; set; }


        /// <summary>
        /// Уровень тейка
        /// </summary>
        public decimal CondPrice { get; set; }
        
        /// <summary>
        /// Цена срабатывания стоп-заявки
        /// </summary>
        public decimal CondPrice2 { get; set; }

        /// <summary>
        /// Цена стоп-заявки
        /// </summary>
        public decimal Price { get; set; }
        /// <summary>
        /// Количество
        /// </summary>
        public int Qty { get; set; }

    }
}
